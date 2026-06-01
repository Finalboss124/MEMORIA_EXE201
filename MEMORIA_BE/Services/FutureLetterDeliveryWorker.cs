using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MEMORIA_BE.Configurations;
using MEMORIA_BE.Data;

namespace MEMORIA_BE.Services;

public sealed class FutureLetterDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FutureLetterDeliverySettings _settings;
    private readonly ILogger<FutureLetterDeliveryWorker> _logger;

    public FutureLetterDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<FutureLetterDeliverySettings> settings,
        ILogger<FutureLetterDeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(10, _settings.PollIntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        await ProcessDueLettersAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessDueLettersAsync(stoppingToken);
        }
    }

    private async Task ProcessDueLettersAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var futureLetterCrypto = scope.ServiceProvider.GetRequiredService<IFutureLetterCrypto>();
            var now = DateTime.UtcNow;
            var batchSize = Math.Clamp(_settings.BatchSize, 1, 100);

            var pendingLogs = await dbContext.ScheduledDeliveryLogs
                .Include(log => log.Recipient)
                .Include(log => log.Letter)
                    .ThenInclude(letter => letter.OwnerUser)
                .Include(log => log.Letter)
                    .ThenInclude(letter => letter.FutureLetterAttachments)
                        .ThenInclude(attachment => attachment.File)
                .Where(log =>
                    log.DeliveryStatus == "Pending" &&
                    log.Letter.SealStatus == "Scheduled" &&
                    log.Letter.DeliveryDate <= now &&
                    log.Recipient.DeliveryChannel == "Email")
                .OrderBy(log => log.Letter.DeliveryDate)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            foreach (var log in pendingLogs)
            {
                log.AttemptCount += 1;
                log.LastAttemptAt = now;

                try
                {
                    if (string.IsNullOrWhiteSpace(log.Recipient.RecipientEmail))
                    {
                        throw new InvalidOperationException("Recipient email is missing.");
                    }

                    var attachments = log.Letter.FutureLetterAttachments
                        .Select(attachment => new FutureLetterEmailAttachment(
                            attachment.File.OriginalFileName,
                            attachment.File.FileUrl,
                            attachment.File.MimeType,
                            attachment.File.FileSizeBytes))
                        .ToList();

                    await emailSender.SendFutureLetterAsync(
                        log.Recipient.RecipientEmail,
                        log.Recipient.RecipientName,
                        log.Letter.OwnerUser.FullName,
                        log.Letter.Title,
                        futureLetterCrypto.Decrypt(log.Letter.BodyEncrypted),
                        log.Letter.DeliveryDate,
                        attachments,
                        cancellationToken);

                    log.DeliveryStatus = "Sent";
                    log.ErrorMessage = null;
                    await UpdateLetterDeliveryStatusAsync(dbContext, log.LetterId, log.DeliveryLogId, now, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.DeliveryStatus = log.AttemptCount >= 3 ? "Failed" : "Pending";
                    log.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    _logger.LogError(ex, "Failed to deliver future letter {LetterId} to recipient {RecipientId}", log.LetterId, log.RecipientId);
                }
            }

            if (pendingLogs.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Future letter delivery worker failed.");
        }
    }

    private static async Task UpdateLetterDeliveryStatusAsync(AppDbContext dbContext, Guid letterId, Guid currentDeliveryLogId, DateTime deliveredAt, CancellationToken cancellationToken)
    {
        var hasOtherPendingLogs = await dbContext.ScheduledDeliveryLogs
            .AnyAsync(log => log.LetterId == letterId && log.DeliveryLogId != currentDeliveryLogId && log.DeliveryStatus == "Pending", cancellationToken);

        if (hasOtherPendingLogs)
        {
            return;
        }

        var letter = await dbContext.FutureLetters.FirstAsync(item => item.LetterId == letterId, cancellationToken);
        letter.SealStatus = "Delivered";
        letter.DeliveredAt = deliveredAt;
    }
}

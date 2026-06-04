using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using MEMORIA_BE.Data;
using MEMORIA_BE.Models;

namespace MEMORIA_BE.Services;

public sealed class LegacyTransferTriggerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LegacyTransferTriggerWorker> _logger;

    public LegacyTransferTriggerWorker(IServiceScopeFactory scopeFactory, ILogger<LegacyTransferTriggerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var now = DateTime.UtcNow;

            var schedules = await dbContext.ProofOfLifeSchedules
                .Include(item => item.LegacyPlan)
                    .ThenInclude(plan => plan.OwnerUser)
                .Include(item => item.ProofOfLifeCheckins)
                .Where(item => item.IsActive && item.LegacyPlan.IsEcontractSigned)
                .ToListAsync(cancellationToken);

            foreach (var schedule in schedules)
            {
                await ProcessScheduleAsync(dbContext, emailSender, schedule, now, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Legacy transfer trigger worker failed.");
        }
    }

    private static async Task ProcessScheduleAsync(
        AppDbContext dbContext,
        IEmailSender emailSender,
        ProofOfLifeSchedule schedule,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var user = schedule.LegacyPlan.OwnerUser;
        if (!user.IsActive)
        {
            return;
        }

        if (schedule.MaxFailedAttempts != 3)
        {
            schedule.MaxFailedAttempts = 3;
        }

        if (user.UserStatus == "SuspectedAbsent")
        {
            await ProcessBeneficiaryEscalationAsync(dbContext, emailSender, schedule.LegacyPlan, now, cancellationToken);
            return;
        }

        var orderedCheckins = schedule.ProofOfLifeCheckins.OrderBy(item => item.SentAt).ToList();
        var latestCheckin = orderedCheckins.LastOrDefault();
        if (latestCheckin is not null && latestCheckin.CheckinStatus == "Sent" && user.LastLoginAt is not null && user.LastLoginAt > latestCheckin.SentAt)
        {
            foreach (var checkin in schedule.ProofOfLifeCheckins.Where(item => item.CheckinStatus == "Sent"))
            {
                checkin.CheckinStatus = "ConfirmedAlive";
                checkin.RespondedAt = user.LastLoginAt;
            }

            schedule.NextCheckAt = user.LastLoginAt.Value.AddDays(schedule.CheckIntervalDays);
            return;
        }

        if (latestCheckin is not null && latestCheckin.CheckinStatus == "Sent")
        {
            if (latestCheckin.ResponseDeadline > now)
            {
                return;
            }

            latestCheckin.CheckinStatus = "Expired";
            latestCheckin.FailureReason = "No response within 7 days.";
        }

        var lastConfirmedAt = orderedCheckins
            .Where(item => item.CheckinStatus == "ConfirmedAlive")
            .Select(item => item.RespondedAt ?? item.SentAt)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        var currentCycleAttempts = orderedCheckins
            .Where(item => item.SentAt > lastConfirmedAt && item.CheckinStatus is "Sent" or "Expired" or "Failed")
            .Count();

        if (currentCycleAttempts >= schedule.MaxFailedAttempts)
        {
            user.UserStatus = "SuspectedAbsent";
            user.UpdatedAt = now;
            schedule.LegacyPlan.PlanStatus = "FrozenPendingLegalVerification";
            schedule.LegacyPlan.UpdatedAt = now;
            await ProcessBeneficiaryEscalationAsync(dbContext, emailSender, schedule.LegacyPlan, now, cancellationToken);
            return;
        }

        if (currentCycleAttempts == 0)
        {
            if (schedule.NextCheckAt <= now)
            {
                await SendPingAsync(dbContext, emailSender, schedule, 1, now, cancellationToken);
            }
            return;
        }

        await SendPingAsync(dbContext, emailSender, schedule, currentCycleAttempts + 1, now, cancellationToken);
    }

    private static async Task ProcessBeneficiaryEscalationAsync(
        AppDbContext dbContext,
        IEmailSender emailSender,
        LegacyPlan plan,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var beneficiaries = await dbContext.Beneficiaries
            .Where(item => item.OwnerUserId == plan.OwnerUserId && item.Email != null)
            .OrderByDescending(item => item.IsPrimary)
            .ThenBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        if (beneficiaries.Count == 0)
        {
            return;
        }

        var requests = await dbContext.LegacyUnlockRequests
            .Include(item => item.LegalDocumentSubmissions)
            .Where(item => item.LegacyPlanId == plan.LegacyPlanId)
            .OrderBy(item => item.SubmittedAt)
            .ToListAsync(cancellationToken);

        if (requests.Any(IsSatisfiedUnlockRequest))
        {
            return;
        }

        var latestRequest = requests.LastOrDefault();
        if (latestRequest is not null && latestRequest.RequestStatus is "Submitted" or "OcrChecking" or "HumanReview")
        {
            if (latestRequest.SubmittedAt.AddDays(7) > now)
            {
                return;
            }

            latestRequest.RequestStatus = "Rejected";
            latestRequest.RequestReason = "Beneficiary did not submit valid documents within 7 days.";
            latestRequest.DecidedAt = now;
        }

        var requestedBeneficiaryIds = requests.Select(item => item.RequestedByBeneficiaryId).ToHashSet();
        var nextBeneficiary = beneficiaries.FirstOrDefault(item => !requestedBeneficiaryIds.Contains(item.BeneficiaryId));
        if (nextBeneficiary?.Email is null)
        {
            return;
        }

        var token = CreateClaimToken();
        var tokenHash = HashToken(token);
        dbContext.LegacyUnlockRequests.Add(new LegacyUnlockRequest
        {
            UnlockRequestId = Guid.NewGuid(),
            LegacyPlanId = plan.LegacyPlanId,
            RequestedByBeneficiaryId = nextBeneficiary.BeneficiaryId,
            RequestStatus = "Submitted",
            RequestReason = "Awaiting legal identity and death-certificate documents.",
            ClaimTokenHash = tokenHash,
            ClaimTokenExpiresAt = now.AddDays(7),
            BeneficiaryNotifiedAt = now,
            SubmittedAt = now
        });

        var claimUrl = $"http://localhost:5500/claim_legacy/code.html?token={Uri.EscapeDataString(token)}";
        await emailSender.SendLegacyPrimaryBeneficiaryNoticeAsync(nextBeneficiary.Email, nextBeneficiary.FullName, plan.OwnerUser.FullName, claimUrl, cancellationToken);
    }

    private static bool IsSatisfiedUnlockRequest(LegacyUnlockRequest request)
    {
        if (request.RequestStatus is "Approved" or "Released")
        {
            return true;
        }

        return request.LegalDocumentSubmissions.Any(item =>
            item.HumanReviewStatus == "Approved" ||
            item.OcrStatus == "Passed");
    }

    private static string CreateClaimToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task SendPingAsync(
        AppDbContext dbContext,
        IEmailSender emailSender,
        ProofOfLifeSchedule schedule,
        int attemptNumber,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (attemptNumber > 3)
        {
            return;
        }

        var responseWindow = schedule.CheckIntervalMinutes > 0
            ? TimeSpan.FromMinutes(schedule.CheckIntervalMinutes)
            : TimeSpan.FromDays(7);
        var deadline = now.Add(responseWindow);
        var checkin = new ProofOfLifeCheckin
        {
            CheckinId = Guid.NewGuid(),
            ScheduleId = schedule.ScheduleId,
            SentAt = now,
            ResponseDeadline = deadline,
            CheckinStatus = "Sent",
            Channel = "Email"
        };
        schedule.ProofOfLifeCheckins.Add(checkin);
        dbContext.ProofOfLifeCheckins.Add(checkin);
        schedule.NextCheckAt = deadline;
        var confirmUrl = $"http://localhost:5284/api/legacy/proof-of-life/confirm/{checkin.CheckinId}";

        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendProofOfLifePingAsync(
            schedule.LegacyPlan.OwnerUser.Email,
            schedule.LegacyPlan.OwnerUser.FullName,
            attemptNumber,
            deadline,
            confirmUrl,
            cancellationToken);
    }
}

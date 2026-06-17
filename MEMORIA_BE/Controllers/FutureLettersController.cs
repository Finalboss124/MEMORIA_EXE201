using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MEMORIA_BE.Data;
using MEMORIA_BE.Models;
using MEMORIA_BE.Services;

namespace MEMORIA_BE.Controllers;

[ApiController]
[Route("api/future-letters")]
public sealed class FutureLettersController : ControllerBase
{
    private const long MaxFileSizeBytes = 450 * 1024 * 1024;
    private static readonly HashSet<string> AllowedMimePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/",
        "video/",
        "text/"
    };

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/zip",
        "application/octet-stream"
    };

    private static readonly HashSet<string> AllowedDeliveryChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Email",
        "SMS",
        "Zalo"
    };

    private readonly AppDbContext _dbContext;
    private readonly ICloudFileStorage _fileStorage;
    private readonly IFutureLetterCrypto _futureLetterCrypto;

    public FutureLettersController(
        AppDbContext dbContext,
        ICloudFileStorage fileStorage,
        IFutureLetterCrypto futureLetterCrypto)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _futureLetterCrypto = futureLetterCrypto;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FutureLetterResponse>>> List(CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var letters = await _dbContext.FutureLetters
            .AsNoTracking()
            .Include(letter => letter.FutureLetterRecipients)
            .Include(letter => letter.FutureLetterAttachments)
                .ThenInclude(attachment => attachment.File)
            .Where(letter => letter.OwnerUserId == ownerUserId)
            .OrderBy(letter => letter.DeliveryDate)
            .ToListAsync(cancellationToken);

        return Ok(letters.Select(ToResponse).ToList());
    }

    [HttpPost]
    [RequestSizeLimit(500_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
    public async Task<ActionResult<FutureLetterResponse>> Create([FromForm] CreateFutureLetterRequest request, CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var title = request.Title?.Trim();
        var recipientName = request.RecipientName?.Trim();
        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = "Title is required." });
        }

        if (string.IsNullOrWhiteSpace(recipientName))
        {
            return BadRequest(new { message = "Recipient name is required." });
        }

        if (request.DeliveryDate <= DateTime.UtcNow)
        {
            return BadRequest(new { message = "Delivery date must be in the future." });
        }

        if (string.IsNullOrWhiteSpace(body) && (request.Files is null || request.Files.Count == 0))
        {
            return BadRequest(new { message = "Add a message or at least one attachment." });
        }

        var deliveryChannel = string.IsNullOrWhiteSpace(request.DeliveryChannel) ? "Email" : request.DeliveryChannel.Trim();
        if (!AllowedDeliveryChannels.Contains(deliveryChannel))
        {
            return BadRequest(new { message = "Delivery channel must be Email, SMS, or Zalo." });
        }

        var recipientEmail = NormalizeOptionalEmail(request.RecipientEmail);
        if (request.Seal && deliveryChannel.Equals("Email", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(recipientEmail))
        {
            return BadRequest(new { message = "Recipient email is required to schedule email delivery." });
        }

        if (!string.IsNullOrWhiteSpace(recipientEmail) && !IsValidEmail(recipientEmail))
        {
            return BadRequest(new { message = "Enter a valid recipient email address." });
        }

        if (request.Seal && request.Files is not null)
        {
            foreach (var file in request.Files)
            {
                if (!IsAllowedFile(file))
                {
                    return BadRequest(new { message = $"File type is not supported: {file.FileName}." });
                }

                if (file.Length > MaxFileSizeBytes)
                {
                    return BadRequest(new { message = $"File is too large: {file.FileName}. Maximum size is 450 MB." });
                }
            }
        }

        var sealStatus = request.Seal ? "Scheduled" : "Draft";
        var now = DateTime.UtcNow;
        var letter = new FutureLetter
        {
            LetterId = Guid.NewGuid(),
            OwnerUserId = ownerUserId.Value,
            Title = title,
            BodyEncrypted = _futureLetterCrypto.Encrypt(body),
            DeliveryDate = request.DeliveryDate.ToUniversalTime(),
            SealStatus = sealStatus,
            IsLocked = request.Seal,
            CreatedAt = now,
            SealedAt = request.Seal ? now : null
        };

        var recipient = new FutureLetterRecipient
        {
            RecipientId = Guid.NewGuid(),
            LetterId = letter.LetterId,
            RecipientName = recipientName,
            RecipientEmail = recipientEmail,
            RecipientPhone = string.IsNullOrWhiteSpace(request.RecipientPhone) ? null : request.RecipientPhone.Trim(),
            RecipientZalo = string.IsNullOrWhiteSpace(request.RecipientZalo) ? null : request.RecipientZalo.Trim(),
            Relationship = string.IsNullOrWhiteSpace(request.Relationship) ? null : request.Relationship.Trim(),
            DeliveryChannel = deliveryChannel,
            CreatedAt = now
        };

        letter.FutureLetterRecipients.Add(recipient);
        if (request.Seal)
        {
            letter.ScheduledDeliveryLogs.Add(new ScheduledDeliveryLog
            {
                DeliveryLogId = Guid.NewGuid(),
                LetterId = letter.LetterId,
                RecipientId = recipient.RecipientId,
                Recipient = recipient,
                DeliveryStatus = "Pending",
                AttemptCount = 0,
                CreatedAt = now
            });
        }

        if (request.Files is not null)
        {
            foreach (var file in request.Files)
            {
                CloudUploadResult uploaded;
                try
                {
                    uploaded = await _fileStorage.UploadAsync(file, ownerUserId.Value, cancellationToken);
                }
                catch (Exception ex)
                {
                    return BadRequest(new { message = $"Unable to upload '{file.FileName}' to Cloudinary. {ex.Message}" });
                }

                var storedFile = new StoredFile
                {
                    FileId = Guid.NewGuid(),
                    OwnerUserId = ownerUserId.Value,
                    OriginalFileName = uploaded.OriginalFileName,
                    StoredFileName = uploaded.StoredFileName,
                    FileUrl = uploaded.FileUrl,
                    MimeType = uploaded.MimeType,
                    FileSizeBytes = uploaded.FileSizeBytes,
                    Sha256Hash = uploaded.Sha256Hash,
                    EncryptionStatus = "Plain",
                    CreatedAt = now
                };

                letter.FutureLetterAttachments.Add(new FutureLetterAttachment
                {
                    LetterAttachmentId = Guid.NewGuid(),
                    LetterId = letter.LetterId,
                    FileId = storedFile.FileId,
                    File = storedFile,
                    CreatedAt = now
                });
            }
        }

        _dbContext.FutureLetters.Add(letter);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), new { id = letter.LetterId }, ToResponse(letter));
    }

    [HttpPut("{letterId:guid}")]
    [RequestSizeLimit(500_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
    public async Task<ActionResult<FutureLetterResponse>> Update(Guid letterId, [FromForm] CreateFutureLetterRequest request, CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var letter = await _dbContext.FutureLetters
            .Include(item => item.FutureLetterRecipients)
            .Include(item => item.FutureLetterAttachments)
                .ThenInclude(attachment => attachment.File)
            .Include(item => item.ScheduledDeliveryLogs)
            .FirstOrDefaultAsync(item => item.LetterId == letterId && item.OwnerUserId == ownerUserId.Value, cancellationToken);

        if (letter is null)
        {
            return NotFound(new { message = "Future letter was not found." });
        }

        if (letter.IsLocked)
        {
            return BadRequest(new { message = "This future letter is already sealed and cannot be edited." });
        }

        var title = request.Title?.Trim();
        var recipientName = request.RecipientName?.Trim();
        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = "Title is required." });
        }

        if (string.IsNullOrWhiteSpace(recipientName))
        {
            return BadRequest(new { message = "Recipient name is required." });
        }

        if (request.DeliveryDate <= DateTime.UtcNow)
        {
            return BadRequest(new { message = "Delivery date must be in the future." });
        }

        if (string.IsNullOrWhiteSpace(body) && (request.Files is null || request.Files.Count == 0) && letter.FutureLetterAttachments.Count == 0)
        {
            return BadRequest(new { message = "Add a message or at least one attachment." });
        }

        var deliveryChannel = string.IsNullOrWhiteSpace(request.DeliveryChannel) ? "Email" : request.DeliveryChannel.Trim();
        if (!AllowedDeliveryChannels.Contains(deliveryChannel))
        {
            return BadRequest(new { message = "Delivery channel must be Email, SMS, or Zalo." });
        }

        var recipientEmail = NormalizeOptionalEmail(request.RecipientEmail);
        if (request.Seal && deliveryChannel.Equals("Email", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(recipientEmail))
        {
            return BadRequest(new { message = "Recipient email is required to schedule email delivery." });
        }

        if (!string.IsNullOrWhiteSpace(recipientEmail) && !IsValidEmail(recipientEmail))
        {
            return BadRequest(new { message = "Enter a valid recipient email address." });
        }

        if (request.Files is not null)
        {
            foreach (var file in request.Files)
            {
                if (!IsAllowedFile(file))
                {
                    return BadRequest(new { message = $"File type is not supported: {file.FileName}." });
                }

                if (file.Length > MaxFileSizeBytes)
                {
                    return BadRequest(new { message = $"File is too large: {file.FileName}. Maximum size is 450 MB." });
                }
            }
        }

        var now = DateTime.UtcNow;
        var encryptedBody = _futureLetterCrypto.Encrypt(body);
        var deliveryDateUtc = request.DeliveryDate.ToUniversalTime();
        var updatedRows = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE FutureLetters
            SET Title = {title},
                BodyEncrypted = {encryptedBody},
                DeliveryDate = {deliveryDateUtc},
                SealStatus = {(request.Seal ? "Scheduled" : "Draft")},
                IsLocked = {request.Seal},
                SealedAt = {(request.Seal ? now : null)}
            WHERE LetterId = {letterId}
              AND OwnerUserId = {ownerUserId.Value}
              AND IsLocked = {false}
            """, cancellationToken);

        if (updatedRows == 0)
        {
            return NotFound(new { message = "This draft no longer exists or has already been sealed." });
        }

        var recipientId = letter.FutureLetterRecipients.FirstOrDefault()?.RecipientId;
        _dbContext.ChangeTracker.Clear();
        if (recipientId is null)
        {
            recipientId = Guid.NewGuid();
            _dbContext.FutureLetterRecipients.Add(new FutureLetterRecipient
            {
                RecipientId = recipientId.Value,
                LetterId = letterId,
                RecipientName = recipientName,
                RecipientEmail = recipientEmail,
                RecipientPhone = string.IsNullOrWhiteSpace(request.RecipientPhone) ? null : request.RecipientPhone.Trim(),
                RecipientZalo = string.IsNullOrWhiteSpace(request.RecipientZalo) ? null : request.RecipientZalo.Trim(),
                Relationship = string.IsNullOrWhiteSpace(request.Relationship) ? null : request.Relationship.Trim(),
                DeliveryChannel = deliveryChannel,
                CreatedAt = now
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE FutureLetterRecipients
                SET RecipientName = {recipientName},
                    RecipientEmail = {recipientEmail},
                    RecipientPhone = {(string.IsNullOrWhiteSpace(request.RecipientPhone) ? null : request.RecipientPhone.Trim())},
                    RecipientZalo = {(string.IsNullOrWhiteSpace(request.RecipientZalo) ? null : request.RecipientZalo.Trim())},
                    Relationship = {(string.IsNullOrWhiteSpace(request.Relationship) ? null : request.Relationship.Trim())},
                    DeliveryChannel = {deliveryChannel}
                WHERE RecipientId = {recipientId.Value}
                """, cancellationToken);
        }

        if (request.Seal && letter.ScheduledDeliveryLogs.Count == 0)
        {
            _dbContext.ScheduledDeliveryLogs.Add(new ScheduledDeliveryLog
            {
                DeliveryLogId = Guid.NewGuid(),
                LetterId = letterId,
                RecipientId = recipientId.Value,
                DeliveryStatus = "Pending",
                AttemptCount = 0,
                CreatedAt = now
            });
        }

        if (request.Seal && request.Files is not null)
        {
            foreach (var file in request.Files)
            {
                CloudUploadResult uploaded;
                try
                {
                    uploaded = await _fileStorage.UploadAsync(file, ownerUserId.Value, cancellationToken);
                }
                catch (Exception ex)
                {
                    return BadRequest(new { message = $"Unable to upload '{file.FileName}' to Cloudinary. {ex.Message}" });
                }

                var storedFile = new StoredFile
                {
                    FileId = Guid.NewGuid(),
                    OwnerUserId = ownerUserId.Value,
                    OriginalFileName = uploaded.OriginalFileName,
                    StoredFileName = uploaded.StoredFileName,
                    FileUrl = uploaded.FileUrl,
                    MimeType = uploaded.MimeType,
                    FileSizeBytes = uploaded.FileSizeBytes,
                    Sha256Hash = uploaded.Sha256Hash,
                    EncryptionStatus = "Plain",
                    CreatedAt = now
                };

                _dbContext.FutureLetterAttachments.Add(new FutureLetterAttachment
                {
                    LetterAttachmentId = Guid.NewGuid(),
                    LetterId = letterId,
                    FileId = storedFile.FileId,
                    File = storedFile,
                    CreatedAt = now
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var refreshed = await LoadLetterForResponse(letterId, ownerUserId.Value, cancellationToken);
        return refreshed is null
            ? NotFound(new { message = "Future letter was not found after saving." })
            : Ok(ToResponse(refreshed));

    }

    [HttpDelete("{letterId:guid}")]
    public async Task<ActionResult> Delete(Guid letterId, CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var letter = await _dbContext.FutureLetters
            .Include(item => item.FutureLetterRecipients)
            .Include(item => item.FutureLetterAttachments)
                .ThenInclude(attachment => attachment.File)
            .Include(item => item.ScheduledDeliveryLogs)
            .FirstOrDefaultAsync(item => item.LetterId == letterId && item.OwnerUserId == ownerUserId.Value, cancellationToken);

        if (letter is null)
        {
            return NotFound(new { message = "Future letter was not found." });
        }

        // Allow deleting sealed letters that have not been delivered yet.
        // Letters that are already delivered cannot be deleted.
        if (letter.DeliveredAt.HasValue || letter.SealStatus == "Delivered")
        {
            return BadRequest(new { message = "This future letter has already been delivered and cannot be deleted." });
        }

        // Remove related entities before removing the letter
        _dbContext.ScheduledDeliveryLogs.RemoveRange(letter.ScheduledDeliveryLogs);
        _dbContext.FutureLetterRecipients.RemoveRange(letter.FutureLetterRecipients);
        _dbContext.FutureLetterAttachments.RemoveRange(letter.FutureLetterAttachments);
        _dbContext.FutureLetters.Remove(letter);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Future letter deleted successfully." });
    }

    [HttpPost("{letterId:guid}/unseal")]
    public async Task<ActionResult<FutureLetterResponse>> Unseal(Guid letterId, CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var letter = await _dbContext.FutureLetters
            .Include(item => item.FutureLetterRecipients)
            .Include(item => item.FutureLetterAttachments)
                .ThenInclude(attachment => attachment.File)
            .Include(item => item.ScheduledDeliveryLogs)
            .FirstOrDefaultAsync(item => item.LetterId == letterId && item.OwnerUserId == ownerUserId.Value, cancellationToken);

        if (letter is null)
        {
            return NotFound(new { message = "Future letter was not found." });
        }

        if (letter.DeliveredAt.HasValue || letter.SealStatus == "Delivered")
        {
            return BadRequest(new { message = "This future letter has already been delivered and cannot be unsealed." });
        }

        if (!letter.IsLocked)
        {
            return BadRequest(new { message = "This letter is already a draft." });
        }

        // Cancel any pending scheduled delivery logs
        _dbContext.ScheduledDeliveryLogs.RemoveRange(letter.ScheduledDeliveryLogs);

        // Reset the letter to Draft
        letter.IsLocked = false;
        letter.SealStatus = "Draft";
        letter.SealedAt = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Reload for response
        var refreshed = await LoadLetterForResponse(letterId, ownerUserId.Value, cancellationToken);
        return refreshed is null
            ? NotFound(new { message = "Future letter was not found after unsealing." })
            : Ok(ToResponse(refreshed));
    }

    private Task<FutureLetter?> LoadLetterForResponse(Guid letterId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        return _dbContext.FutureLetters
            .AsNoTracking()
            .Include(item => item.FutureLetterRecipients)
            .Include(item => item.FutureLetterAttachments)
                .ThenInclude(attachment => attachment.File)
            .FirstOrDefaultAsync(item => item.LetterId == letterId && item.OwnerUserId == ownerUserId, cancellationToken);
    }

    private Guid? GetCurrentUserId()
    {
        var value =
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub") ??
            User.FindFirstValue("nameid") ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            TryReadUserIdFromBearerToken() ??
            Request.Headers["X-Memoria-User-Id"].FirstOrDefault();

        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private string? TryReadUserIdFromBearerToken()
    {
        var authorization = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(claim =>
                claim.Type == JwtRegisteredClaimNames.Sub ||
                claim.Type == "sub" ||
                claim.Type == "nameid" ||
                claim.Type == ClaimTypes.NameIdentifier)?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAllowedFile(IFormFile file)
    {
        if (file.Length <= 0)
        {
            return false;
        }

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        return AllowedMimeTypes.Contains(contentType) ||
            AllowedMimePrefixes.Any(prefix => contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeOptionalEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);
            var domain = address.Host;
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase) &&
                domain.Contains('.') &&
                !domain.StartsWith('.') &&
                !domain.EndsWith('.');
        }
        catch
        {
            return false;
        }
    }

    private FutureLetterResponse ToResponse(FutureLetter letter)
    {
        return new FutureLetterResponse(
            letter.LetterId,
            letter.Title,
            _futureLetterCrypto.Decrypt(letter.BodyEncrypted),
            letter.DeliveryDate,
            letter.SealStatus,
            letter.IsLocked,
            letter.CreatedAt,
            letter.SealedAt,
            letter.DeliveredAt,
            letter.FutureLetterRecipients.Select(recipient => new FutureLetterRecipientResponse(
                recipient.RecipientId,
                recipient.RecipientName,
                recipient.RecipientEmail,
                recipient.RecipientPhone,
                recipient.RecipientZalo,
                recipient.Relationship,
                recipient.DeliveryChannel
            )).ToList(),
            letter.FutureLetterAttachments.Select(attachment => new FutureLetterAttachmentResponse(
                attachment.File.FileId,
                attachment.File.OriginalFileName,
                attachment.File.FileUrl,
                attachment.File.MimeType,
                attachment.File.FileSizeBytes
            )).ToList());
    }
}

public sealed class CreateFutureLetterRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public DateTime DeliveryDate { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientEmail { get; set; }
    public string? RecipientPhone { get; set; }
    public string? RecipientZalo { get; set; }
    public string? Relationship { get; set; }
    public string? DeliveryChannel { get; set; }
    public bool Seal { get; set; }
    public List<IFormFile>? Files { get; set; }
}

public sealed record FutureLetterResponse(
    Guid LetterId,
    string Title,
    string? Body,
    DateTime DeliveryDate,
    string SealStatus,
    bool IsLocked,
    DateTime CreatedAt,
    DateTime? SealedAt,
    DateTime? DeliveredAt,
    IReadOnlyList<FutureLetterRecipientResponse> Recipients,
    IReadOnlyList<FutureLetterAttachmentResponse> Attachments);

public sealed record FutureLetterRecipientResponse(
    Guid RecipientId,
    string RecipientName,
    string? RecipientEmail,
    string? RecipientPhone,
    string? RecipientZalo,
    string? Relationship,
    string DeliveryChannel);

public sealed record FutureLetterAttachmentResponse(
    Guid FileId,
    string OriginalFileName,
    string FileUrl,
    string MimeType,
    long FileSizeBytes);

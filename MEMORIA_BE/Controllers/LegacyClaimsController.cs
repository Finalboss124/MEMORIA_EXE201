using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MEMORIA_BE.Data;
using MEMORIA_BE.Models;
using MEMORIA_BE.Services;

namespace MEMORIA_BE.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/legacy-claims")]
public sealed class LegacyClaimsController : ControllerBase
{
    private const string ClaimOtpPurpose = "LegacyClaimOtp";
    private readonly AppDbContext _dbContext;
    private readonly IEmailSender _emailSender;
    private readonly IWebHostEnvironment _environment;
    private readonly PasswordHasher<AuthVerificationCode> _codeHasher = new();

    public LegacyClaimsController(AppDbContext dbContext, IEmailSender emailSender, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
        _environment = environment;
    }

    [HttpGet("{token}")]
    public async Task<ActionResult<ClaimLegacyInfoResponse>> GetInfo(string token, CancellationToken cancellationToken)
    {
        var request = await LoadRequestByTokenAsync(token, cancellationToken);
        if (request is null)
        {
            return NotFound(new { message = "This secure claim link is invalid or expired." });
        }

        return Ok(new ClaimLegacyInfoResponse(
            request.LegacyPlan.OwnerUser.FullName,
            request.RequestedByBeneficiary.FullName,
            MaskContact(request.RequestedByBeneficiary.Email ?? request.RequestedByBeneficiary.PhoneNumber)));
    }

    [HttpPost("{token}/verify-identity")]
    public async Task<ActionResult<ClaimIdentityResponse>> VerifyIdentity(string token, ClaimIdentityRequest body, CancellationToken cancellationToken)
    {
        var request = await LoadRequestByTokenAsync(token, cancellationToken);
        if (request is null)
        {
            return NotFound(new { message = "This secure claim link is invalid or expired." });
        }

        var beneficiary = request.RequestedByBeneficiary;
        if (!beneficiary.FullName.Trim().Equals((body.FullName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) ||
            beneficiary.IdentityDocumentHash != HashIdentityDocument(body.IdentityDocumentNumber ?? string.Empty))
        {
            return Unauthorized(new { message = "Name or identity document number does not match the beneficiary configured by the account owner." });
        }

        request.BeneficiaryVerifiedAt = DateTime.UtcNow;
        request.RequestStatus = "OcrChecking";
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ClaimIdentityResponse(true));
    }

    [HttpPost("{token}/documents")]
    [RequestSizeLimit(30_000_000)]
    public async Task<ActionResult> UploadDocuments(string token, [FromForm] IFormFile deathCertificate, [FromForm] List<IFormFile> identityDocuments, CancellationToken cancellationToken)
    {
        var request = await LoadRequestByTokenAsync(token, cancellationToken, asNoTracking: true);
        if (request is null)
        {
            return NotFound(new { message = "This secure claim link is invalid or expired." });
        }

        if (request.BeneficiaryVerifiedAt is null)
        {
            return BadRequest(new { message = "Please verify your identity before uploading documents." });
        }

        if (!IsAllowedClaimFile(deathCertificate, allowDocx: false) ||
            identityDocuments is null ||
            identityDocuments.Count == 0 ||
            identityDocuments.Any(file => !IsAllowedClaimFile(file, allowDocx: false)))
        {
            return BadRequest(new { message = "Please upload image or PDF files only." });
        }

        await SaveLegalDocumentAsync(request, deathCertificate, "DeathCertificate", cancellationToken);
        foreach (var identityDocument in identityDocuments)
        {
            await SaveLegalDocumentAsync(request, identityDocument, "BeneficiaryIdentityDocument", cancellationToken);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _dbContext.LegacyUnlockRequests
            .Where(item => item.UnlockRequestId == request.UnlockRequestId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RequestStatus, "HumanReview"),
                cancellationToken);

        return Ok(new { message = "Documents uploaded." });
    }

    [HttpPost("{token}/request-otp")]
    public async Task<ActionResult<ClaimOtpResponse>> RequestOtp(string token, CancellationToken cancellationToken)
    {
        var request = await LoadRequestByTokenAsync(token, cancellationToken);
        if (request is null)
        {
            return NotFound(new { message = "This secure claim link is invalid or expired." });
        }

        var beneficiary = request.RequestedByBeneficiary;
        if (string.IsNullOrWhiteSpace(beneficiary.Email))
        {
            return BadRequest(new { message = "This beneficiary does not have an email for OTP verification." });
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var verification = new AuthVerificationCode
        {
            VerificationId = Guid.NewGuid(),
            UserId = request.LegacyPlan.OwnerUserId,
            Purpose = ClaimOtpPurpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        };
        verification.CodeHash = _codeHasher.HashPassword(verification, code);
        _dbContext.AuthVerificationCodes.Add(verification);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _emailSender.SendOtpAsync(beneficiary.Email, beneficiary.FullName, code, ClaimOtpPurpose, cancellationToken);

        return Ok(new ClaimOtpResponse(verification.VerificationId, MaskContact(beneficiary.Email)));
    }

    [HttpPost("{token}/submit")]
    public async Task<ActionResult> SubmitClaim(string token, SubmitClaimRequest body, CancellationToken cancellationToken)
    {
        var request = await LoadRequestByTokenAsync(token, cancellationToken);
        if (request is null)
        {
            return NotFound(new { message = "This secure claim link is invalid or expired." });
        }

        var verification = await _dbContext.AuthVerificationCodes.FirstOrDefaultAsync(item =>
            item.VerificationId == body.VerificationId &&
            item.UserId == request.LegacyPlan.OwnerUserId &&
            item.Purpose == ClaimOtpPurpose,
            cancellationToken);
        if (verification is null || verification.ConsumedAt is not null || verification.ExpiresAt < DateTime.UtcNow)
        {
            return Unauthorized(new { message = "OTP is invalid or expired." });
        }

        var result = _codeHasher.VerifyHashedPassword(verification, verification.CodeHash, (body.Code ?? string.Empty).Trim());
        if (result is not PasswordVerificationResult.Success and not PasswordVerificationResult.SuccessRehashNeeded)
        {
            return Unauthorized(new { message = "OTP is incorrect." });
        }

        verification.ConsumedAt = DateTime.UtcNow;
        request.RequestStatus = "HumanReview";
        request.RequestReason = "Claim submitted for Memoria admin review.";
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Your claim has been submitted to Memoria admins for manual review." });
    }

    private async Task<LegacyUnlockRequest?> LoadRequestByTokenAsync(string token, CancellationToken cancellationToken, bool asNoTracking = false)
    {
        var tokenHash = HashToken(token);
        var query = _dbContext.LegacyUnlockRequests
            .Include(item => item.LegacyPlan)
                .ThenInclude(plan => plan.OwnerUser)
            .Include(item => item.RequestedByBeneficiary)
            .Include(item => item.LegalDocumentSubmissions)
            .AsQueryable();

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(item =>
            item.ClaimTokenHash == tokenHash &&
            item.ClaimTokenExpiresAt != null &&
            item.ClaimTokenExpiresAt > DateTime.UtcNow &&
            item.RequestStatus != "Rejected",
            cancellationToken);
    }

    private async Task SaveLegalDocumentAsync(LegacyUnlockRequest request, IFormFile file, string documentType, CancellationToken cancellationToken)
    {
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var relativeDirectory = Path.Combine("local-uploads", "legacy-claims", request.UnlockRequestId.ToString("N"));
        var absoluteDirectory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteDirectory, storedFileName);
        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var storedFile = new StoredFile
        {
            FileId = Guid.NewGuid(),
            OwnerUserId = request.LegacyPlan.OwnerUserId,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            FileUrl = $"{baseUrl}/{relativeDirectory.Replace('\\', '/')}/{Uri.EscapeDataString(storedFileName)}",
            MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileSizeBytes = file.Length,
            StoragePurpose = "legacy-claim",
            EncryptionStatus = "Plain",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.StoredFiles.Add(storedFile);
        _dbContext.LegalDocumentSubmissions.Add(new LegalDocumentSubmission
        {
            LegalDocumentId = Guid.NewGuid(),
            UnlockRequestId = request.UnlockRequestId,
            DocumentType = documentType,
            FileId = storedFile.FileId,
            OcrStatus = "Pending",
            HumanReviewStatus = "Pending",
            CreatedAt = DateTime.UtcNow
        });
    }

    private static bool IsAllowedClaimFile(IFormFile? file, bool allowDocx)
    {
        if (file is null || file.Length <= 0 || file.Length > 30_000_000)
        {
            return false;
        }

        var extension = Path.GetExtension(file.FileName);
        var allowed = allowDocx
            ? new[] { ".png", ".jpg", ".jpeg", ".pdf", ".docx" }
            : new[] { ".png", ".jpg", ".jpeg", ".pdf" };
        return allowed.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashIdentityDocument(string value)
    {
        var normalized = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string MaskContact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "configured contact";
        }

        var parts = value.Split('@', 2);
        if (parts.Length == 2 && parts[0].Length > 2)
        {
            return $"{parts[0][0]}***{parts[0][^1]}@{parts[1]}";
        }

        return value.Length <= 4 ? "****" : $"{new string('*', value.Length - 4)}{value[^4..]}";
    }
}

public sealed record ClaimLegacyInfoResponse(string OwnerName, string BeneficiaryName, string ContactMasked);
public sealed record ClaimIdentityRequest(string? FullName, string? IdentityDocumentNumber);
public sealed record ClaimIdentityResponse(bool IsVerified);
public sealed record ClaimOtpResponse(Guid VerificationId, string ContactMasked);
public sealed record SubmitClaimRequest(Guid VerificationId, string? Code);

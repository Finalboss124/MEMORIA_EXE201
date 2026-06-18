using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MEMORIA_BE.Configurations;
using MEMORIA_BE.Data;
using MEMORIA_BE.Models;
using MEMORIA_BE.Services;

namespace MEMORIA_BE.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IEmailSender _emailSender;
    private readonly FrontendSettings _frontendSettings;

    public AdminController(AppDbContext dbContext, IEmailSender emailSender, IOptions<FrontendSettings> frontendSettings)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
        _frontendSettings = frontendSettings.Value;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var now = DateTime.UtcNow;
        var requests = await _dbContext.LegacyUnlockRequests.AsNoTracking().ToListAsync(cancellationToken);
        var users = await _dbContext.Users.AsNoTracking().ToListAsync(cancellationToken);

        return Ok(new AdminDashboardResponse(
            requests.Count(item => item.RequestStatus is "Submitted" or "OcrChecking" or "HumanReview"),
            requests.Count(item => item.RequestStatus is "Approved" or "Released"),
            requests.Count(item => item.RequestStatus == "Rejected"),
            users.Count(item => item.IsActive),
            users.Count(item => !item.IsActive),
            requests.Count(item => item.SubmittedAt >= now.AddDays(-7)),
            requests.Count(item => item.RequestStatus is "Submitted" or "OcrChecking" or "HumanReview" && item.SubmittedAt.AddDays(7) <= now)));
    }

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyCollection<AdminUserResponse>>> ListUsers(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var users = await _dbContext.Users
            .AsNoTracking()
            .Include(item => item.UserRoles)
                .ThenInclude(item => item.Role)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new AdminUserResponse(
                item.UserId,
                item.FullName,
                item.Email,
                item.PhoneNumber,
                item.IsActive,
                item.UserStatus,
                item.LastLoginAt,
                item.CreatedAt,
                item.UserRoles
                    .Select(role => role.Role != null ? role.Role.RoleName : null)
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Select(role => role!)
                    .ToArray()))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpPost("users/{userId:guid}/lock")]
    public async Task<IActionResult> LockUser(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        return await SetUserActiveStateAsync(userId, false, cancellationToken);
    }

    [HttpPost("users/{userId:guid}/unlock")]
    public async Task<IActionResult> UnlockUser(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        return await SetUserActiveStateAsync(userId, true, cancellationToken);
    }

    [HttpGet("legacy-requests")]
    public async Task<ActionResult<IReadOnlyCollection<AdminLegacyRequestRowResponse>>> ListLegacyRequests(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var rows = await _dbContext.LegacyUnlockRequests
            .AsNoTracking()
            .Include(item => item.LegacyPlan)
                .ThenInclude(item => item.OwnerUser)
            .Include(item => item.RequestedByBeneficiary)
            .OrderByDescending(item => item.SubmittedAt)
            .Select(item => new AdminLegacyRequestRowResponse(
                item.UnlockRequestId,
                FormatRequestCode(item.UnlockRequestId),
                item.LegacyPlan.OwnerUser.FullName,
                item.RequestedByBeneficiary.FullName,
                item.RequestedByBeneficiary.Relationship,
                item.SubmittedAt,
                item.RequestStatus,
                item.RequestReason))
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("legacy-requests/{requestId:guid}")]
    public async Task<ActionResult<AdminLegacyRequestDetailResponse>> GetLegacyRequest(Guid requestId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var request = await LoadRequestAsync(requestId, cancellationToken);
        if (request is null)
        {
            return NotFound(new { message = "Legacy request was not found." });
        }

        return Ok(ToDetailResponse(request));
    }

    [HttpPost("legacy-requests/{requestId:guid}/approve")]
    public async Task<ActionResult<AdminLegacyRequestDetailResponse>> ApproveLegacyRequest(Guid requestId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var request = await LoadRequestAsync(requestId, cancellationToken);
        if (request is null)
        {
            return NotFound(new { message = "Legacy request was not found." });
        }

        if (request.RequestStatus is "Approved" or "Released")
        {
            return BadRequest(new { message = "This legacy request has already been approved." });
        }

        if (request.RequestStatus == "Rejected")
        {
            return BadRequest(new { message = "This legacy request has already been rejected." });
        }

        request.RequestStatus = "Approved";
        request.RequestReason = "Approved by Memoria admin.";
        request.DecidedAt = DateTime.UtcNow;
        request.LegacyPlan.PlanStatus = "Released";
        request.LegacyPlan.OwnerUser.IsActive = false;
        request.LegacyPlan.OwnerUser.UserStatus = "Closed";
        request.LegacyPlan.OwnerUser.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.RequestedByBeneficiary.Email))
        {
            var files = await _dbContext.StoredFiles
                .AsNoTracking()
                .Where(item =>
                    item.OwnerUserId == request.LegacyPlan.OwnerUserId &&
                    item.StoragePurpose != "legacy-claim")
                .OrderBy(item => item.StoragePurpose)
                .ThenByDescending(item => item.CreatedAt)
                .Select(item => new LegacyReleaseFile(
                    item.OriginalFileName,
                    item.FileUrl,
                    item.StoragePurpose ?? "legacy-asset",
                    item.FileSizeBytes))
                .ToListAsync(cancellationToken);

            await _emailSender.SendLegacyReleaseAsync(
                request.RequestedByBeneficiary.Email,
                request.RequestedByBeneficiary.FullName,
                request.LegacyPlan.OwnerUser.FullName,
                files,
                cancellationToken);
        }

        return Ok(ToDetailResponse(request));
    }

    [HttpPost("legacy-requests/{requestId:guid}/reject")]
    public async Task<ActionResult<AdminLegacyRequestDetailResponse>> RejectLegacyRequest(Guid requestId, RejectLegacyRequestBody body, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var request = await LoadRequestAsync(requestId, cancellationToken);
        if (request is null)
        {
            return NotFound(new { message = "Legacy request was not found." });
        }

        if (request.RequestStatus is "Approved" or "Released")
        {
            return BadRequest(new { message = "Approved legacy requests cannot be rejected." });
        }

        if (request.RequestStatus == "Rejected")
        {
            return BadRequest(new { message = "This legacy request has already been rejected." });
        }

        var reason = string.IsNullOrWhiteSpace(body.Reason)
            ? "Documents need to be resubmitted."
            : body.Reason.Trim();
        var token = CreateClaimToken();
        request.ClaimTokenHash = HashToken(token);
        request.ClaimTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        request.RequestStatus = "Submitted";
        request.RequestReason = reason;
        request.DecidedAt = null;
        request.BeneficiaryNotifiedAt = DateTime.UtcNow;
        foreach (var doc in request.LegalDocumentSubmissions)
        {
            doc.HumanReviewStatus = "Rejected";
            doc.RejectReason = reason;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.RequestedByBeneficiary.Email))
        {
            var claimUrl = BuildClaimUrl(token);
            await _emailSender.SendLegacyClaimRejectedAsync(
                request.RequestedByBeneficiary.Email,
                request.RequestedByBeneficiary.FullName,
                request.LegacyPlan.OwnerUser.FullName,
                reason,
                claimUrl,
                cancellationToken);
        }

        return Ok(ToDetailResponse(request));
    }

    private async Task<IActionResult> SetUserActiveStateAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == userId && !isActive)
        {
            return BadRequest(new { message = "You cannot lock your own admin account." });
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (user is null)
        {
            return NotFound(new { message = "User was not found." });
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        if (isActive && user.UserStatus == "Closed")
        {
            user.UserStatus = "Active";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { user.UserId, user.IsActive, user.UserStatus });
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return false;
        }

        return await _dbContext.UserRoles
            .AsNoTracking()
            .AnyAsync(item =>
                item.UserId == userId.Value &&
                item.Role.RoleName == "Admin",
                cancellationToken);
    }

    private async Task<LegacyUnlockRequest?> LoadRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        return await _dbContext.LegacyUnlockRequests
            .Include(item => item.LegacyPlan)
                .ThenInclude(item => item.OwnerUser)
            .Include(item => item.RequestedByBeneficiary)
            .Include(item => item.LegalDocumentSubmissions)
                .ThenInclude(item => item.File)
            .FirstOrDefaultAsync(item => item.UnlockRequestId == requestId, cancellationToken);
    }

    private static AdminLegacyRequestDetailResponse ToDetailResponse(LegacyUnlockRequest request)
    {
        var owner = request.LegacyPlan.OwnerUser;
        var beneficiary = request.RequestedByBeneficiary;
        var docs = request.LegalDocumentSubmissions
            .OrderBy(item => item.CreatedAt)
            .Select(item => new AdminClaimDocumentResponse(
                item.LegalDocumentId,
                item.DocumentType,
                item.File.OriginalFileName,
                item.File.FileUrl,
                item.File.MimeType,
                item.File.FileSizeBytes,
                item.OcrStatus,
                item.HumanReviewStatus,
                item.RejectReason,
                item.CreatedAt))
            .ToArray();

        return new AdminLegacyRequestDetailResponse(
            request.UnlockRequestId,
            FormatRequestCode(request.UnlockRequestId),
            request.RequestStatus,
            request.RequestReason,
            request.SubmittedAt,
            request.DecidedAt,
            new AdminClaimOwnerResponse(owner.UserId, owner.FullName, owner.Email, owner.CccdNumber, owner.UserStatus, owner.IsActive),
            new AdminClaimBeneficiaryResponse(beneficiary.BeneficiaryId, beneficiary.FullName, beneficiary.Relationship, beneficiary.Email, beneficiary.PhoneNumber, beneficiary.IdentityDocumentMasked, beneficiary.IsPrimary),
            new AdminClaimSubmittedIdentityResponse(beneficiary.FullName, beneficiary.IdentityDocumentMasked, beneficiary.Email ?? beneficiary.PhoneNumber, request.BeneficiaryVerifiedAt),
            docs);
    }

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (Guid.TryParse(value, out var userId))
        {
            return userId;
        }

        var headerValue = Request.Headers["X-Memoria-User-Id"].FirstOrDefault();
        return Guid.TryParse(headerValue, out userId) ? userId : null;
    }

    private static string FormatRequestCode(Guid id) => $"#REQ-{Math.Abs(id.GetHashCode()) % 10000:0000}";

    private string BuildClaimUrl(string token)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_frontendSettings.BaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : _frontendSettings.BaseUrl.TrimEnd('/');

        return $"{baseUrl}/claim_legacy/code.html?token={Uri.EscapeDataString(token)}";
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
}

public sealed record AdminDashboardResponse(int PendingReview, int Approved, int Rejected, int ActiveUsers, int LockedUsers, int RequestsThisWeek, int AtRisk);
public sealed record AdminUserResponse(Guid UserId, string FullName, string Email, string? PhoneNumber, bool IsActive, string UserStatus, DateTime? LastLoginAt, DateTime CreatedAt, IReadOnlyCollection<string> Roles);
public sealed record AdminLegacyRequestRowResponse(Guid UnlockRequestId, string RequestCode, string OwnerName, string BeneficiaryName, string Relationship, DateTime SubmittedAt, string RequestStatus, string? RequestReason);
public sealed record AdminLegacyRequestDetailResponse(Guid UnlockRequestId, string RequestCode, string RequestStatus, string? RequestReason, DateTime SubmittedAt, DateTime? DecidedAt, AdminClaimOwnerResponse Owner, AdminClaimBeneficiaryResponse Beneficiary, AdminClaimSubmittedIdentityResponse SubmittedIdentity, IReadOnlyCollection<AdminClaimDocumentResponse> Documents);
public sealed record AdminClaimOwnerResponse(Guid UserId, string FullName, string Email, string? CccdNumber, string UserStatus, bool IsActive);
public sealed record AdminClaimBeneficiaryResponse(Guid BeneficiaryId, string FullName, string Relationship, string? Email, string? PhoneNumber, string? IdentityDocumentMasked, bool IsPrimary);
public sealed record AdminClaimSubmittedIdentityResponse(string FullName, string? IdentityDocumentMasked, string? Contact, DateTime? VerifiedAt);
public sealed record AdminClaimDocumentResponse(Guid LegalDocumentId, string DocumentType, string FileName, string FileUrl, string MimeType, long FileSizeBytes, string OcrStatus, string HumanReviewStatus, string? RejectReason, DateTime CreatedAt);
public sealed record RejectLegacyRequestBody(string? Reason);

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MEMORIA_BE.Data;
using MEMORIA_BE.Models;
using MEMORIA_BE.Services;

namespace MEMORIA_BE.Controllers;

[ApiController]
[Route("api/legacy")]
public sealed class LegacyController : ControllerBase
{
    private const string ContractOtpPurpose = "LegacyContract";
    private const int OtpMinutes = 10;
    private const int MaxAttempts = 5;
    private const long MaxStorageFileBytes = 200 * 1024 * 1024;

    private static readonly Dictionary<string, StorageBucketDefinition> StorageBuckets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["family-vault"] = new StorageBucketDefinition(
            "family-vault",
            "Family Vaults",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".mp4", ".mp3" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "video/mp4", "audio/mpeg", "audio/mp3" }),
        ["legal-documents"] = new StorageBucketDefinition(
            "legal-documents",
            "Legal Documents",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" })
    };

    private readonly AppDbContext _dbContext;
    private readonly IEmailSender _emailSender;
    private readonly PasswordHasher<AuthVerificationCode> _codeHasher = new();
    private readonly IWebHostEnvironment _environment;

    public LegacyController(AppDbContext dbContext, IEmailSender emailSender, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
        _environment = environment;
    }

    [HttpGet("contract")]
    public async Task<ActionResult<LegacyContractResponse>> GetContract(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId.Value && item.IsActive, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var plan = await GetOrCreateDefaultPlanAsync(user.UserId, cancellationToken);
        return Ok(new LegacyContractResponse(
            plan.LegacyPlanId,
            plan.IsEcontractSigned,
            plan.ContractSignedAt,
            ApplyUserInfo(RewriteArticleFive(LoadContractText()), user)));
    }

    [HttpPost("contract/request-otp")]
    public async Task<ActionResult<LegacyContractOtpResponse>> RequestContractOtp(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.UserId == userId.Value && item.IsActive, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var plan = await GetOrCreateDefaultPlanAsync(user.UserId, cancellationToken);
        if (plan.IsEcontractSigned)
        {
            return Ok(new LegacyContractOtpResponse(null, MaskEmail(user.Email), plan.ContractSignedAt));
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var verification = new AuthVerificationCode
        {
            VerificationId = Guid.NewGuid(),
            UserId = user.UserId,
            Purpose = ContractOtpPurpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpMinutes),
            CreatedAt = DateTime.UtcNow
        };
        verification.CodeHash = _codeHasher.HashPassword(verification, code);

        _dbContext.AuthVerificationCodes.Add(verification);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _emailSender.SendOtpAsync(user.Email, user.FullName, code, ContractOtpPurpose, cancellationToken);

        return Ok(new LegacyContractOtpResponse(verification.VerificationId, MaskEmail(user.Email), verification.ExpiresAt));
    }

    [HttpPost("contract/verify-otp")]
    public async Task<ActionResult<LegacyContractSignedResponse>> VerifyContractOtp(VerifyLegacyContractOtpRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var verification = await _dbContext.AuthVerificationCodes
            .Include(item => item.User)
            .FirstOrDefaultAsync(item =>
                item.VerificationId == request.VerificationId &&
                item.UserId == userId.Value &&
                item.Purpose == ContractOtpPurpose,
                cancellationToken);

        if (verification is null || verification.ConsumedAt is not null || verification.ExpiresAt < DateTime.UtcNow)
        {
            return Unauthorized(new { message = "The verification code has expired or is invalid." });
        }

        if (verification.AttemptCount >= MaxAttempts)
        {
            return Unauthorized(new { message = "Too many incorrect attempts. Please request a new code." });
        }

        verification.AttemptCount += 1;
        var result = _codeHasher.VerifyHashedPassword(verification, verification.CodeHash, request.Code.Trim());
        if (result is not PasswordVerificationResult.Success and not PasswordVerificationResult.SuccessRehashNeeded)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Unauthorized(new { message = "The verification code is incorrect." });
        }

        var plan = await GetOrCreateDefaultPlanAsync(userId.Value, cancellationToken);
        var now = DateTime.UtcNow;
        verification.ConsumedAt = now;
        plan.IsEcontractSigned = true;
        plan.ContractSignedAt = now;
        plan.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new LegacyContractSignedResponse(true, now));
    }

    [HttpGet("beneficiaries")]
    public async Task<ActionResult<IReadOnlyList<LegacyBeneficiaryResponse>>> ListBeneficiaries(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var beneficiaries = await _dbContext.Beneficiaries
            .AsNoTracking()
            .Where(item => item.OwnerUserId == userId.Value)
            .OrderByDescending(item => item.IsPrimary)
            .ThenBy(item => item.CreatedAt)
            .Select(item => new LegacyBeneficiaryResponse(
                item.BeneficiaryId,
                item.FullName,
                item.Email,
                item.PhoneNumber,
                item.Relationship,
                item.IdentityDocumentMasked,
                item.IsPrimary,
                item.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(beneficiaries);
    }

    [HttpPost("beneficiaries")]
    public async Task<ActionResult<LegacyBeneficiaryResponse>> CreateBeneficiary(CreateLegacyBeneficiaryRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var fullName = request.FullName?.Trim();
        var relationship = request.Relationship?.Trim();
        var identityDocument = request.IdentityDocumentNumber?.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return BadRequest(new { message = "Beneficiary name is required." });
        }

        if (string.IsNullOrWhiteSpace(relationship))
        {
            return BadRequest(new { message = "Relationship is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new { message = "Add at least an email or phone number for OTP contact." });
        }

        if (string.IsNullOrWhiteSpace(identityDocument))
        {
            return BadRequest(new { message = "Identity document number is required." });
        }

        var now = DateTime.UtcNow;
        if (request.IsPrimary)
        {
            var primaryBeneficiaries = await _dbContext.Beneficiaries
                .Where(item => item.OwnerUserId == userId.Value && item.IsPrimary)
                .ToListAsync(cancellationToken);
            foreach (var item in primaryBeneficiaries)
            {
                item.IsPrimary = false;
            }
        }

        var beneficiary = new Beneficiary
        {
            BeneficiaryId = Guid.NewGuid(),
            OwnerUserId = userId.Value,
            FullName = fullName,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Relationship = relationship,
            IdentityDocumentMasked = MaskIdentityDocument(identityDocument),
            IdentityDocumentHash = HashIdentityDocument(identityDocument),
            IsPrimary = request.IsPrimary,
            CreatedAt = now
        };

        _dbContext.Beneficiaries.Add(beneficiary);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(ListBeneficiaries), new { id = beneficiary.BeneficiaryId }, ToBeneficiaryResponse(beneficiary));
    }

    [HttpGet("storage/{bucket}")]
    public async Task<ActionResult<IReadOnlyList<LegacyStoredFileResponse>>> ListStorageFiles(string bucket, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!StorageBuckets.TryGetValue(bucket, out var definition))
        {
            return NotFound(new { message = "Storage bucket was not found." });
        }

        var files = await _dbContext.StoredFiles
            .AsNoTracking()
            .Where(item => item.OwnerUserId == userId.Value && item.StoragePurpose == definition.Key)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new LegacyStoredFileResponse(
                item.FileId,
                item.OriginalFileName,
                item.FileUrl,
                item.MimeType,
                item.FileSizeBytes,
                item.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(files);
    }

    [HttpPost("storage/{bucket}")]
    [RequestSizeLimit(MaxStorageFileBytes)]
    public async Task<ActionResult<LegacyStoredFileResponse>> UploadStorageFile(string bucket, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!StorageBuckets.TryGetValue(bucket, out var definition))
        {
            return NotFound(new { message = "Storage bucket was not found." });
        }

        if (file.Length <= 0)
        {
            return BadRequest(new { message = "Please choose a file to upload." });
        }

        if (file.Length > MaxStorageFileBytes)
        {
            return BadRequest(new { message = "File must be 200 MB or smaller." });
        }

        var extension = Path.GetExtension(file.FileName);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        if (!definition.AllowedExtensions.Contains(extension) || !definition.AllowedMimeTypes.Contains(contentType))
        {
            return BadRequest(new { message = $"This file type is not supported for {definition.DisplayName}." });
        }

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var relativeDirectory = Path.Combine("local-uploads", "legacy-storage", userId.Value.ToString("N"), definition.Key);
        var absoluteDirectory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var absolutePath = Path.Combine(absoluteDirectory, storedFileName);
        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var sha256 = await ComputeSha256Async(absolutePath, cancellationToken);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var storedFile = new StoredFile
        {
            FileId = Guid.NewGuid(),
            OwnerUserId = userId.Value,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            FileUrl = $"{baseUrl}/{relativeDirectory.Replace('\\', '/')}/{Uri.EscapeDataString(storedFileName)}",
            MimeType = contentType,
            FileSizeBytes = file.Length,
            Sha256Hash = sha256,
            StoragePurpose = definition.Key,
            EncryptionStatus = "Plain",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.StoredFiles.Add(storedFile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(ListStorageFiles), new { bucket = definition.Key }, ToStoredFileResponse(storedFile));
    }

    [HttpDelete("storage/{bucket}/{fileId:guid}")]
    public async Task<IActionResult> DeleteStorageFile(string bucket, Guid fileId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!StorageBuckets.TryGetValue(bucket, out var definition))
        {
            return NotFound(new { message = "Storage bucket was not found." });
        }

        var file = await _dbContext.StoredFiles
            .FirstOrDefaultAsync(item => item.FileId == fileId && item.OwnerUserId == userId.Value && item.StoragePurpose == definition.Key, cancellationToken);
        if (file is null)
        {
            return NotFound(new { message = "File was not found." });
        }

        TryDeleteLocalStoredFile(file);
        _dbContext.StoredFiles.Remove(file);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("transfer-trigger")]
    public async Task<ActionResult<TransferTriggerResponse>> GetTransferTrigger(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var plan = await GetOrCreateDefaultPlanAsync(userId.Value, cancellationToken);
        var schedule = await GetOrCreateTransferScheduleAsync(plan, user.LastLoginAt ?? user.CreatedAt, cancellationToken);
        return Ok(ToTransferTriggerResponse(schedule, user.UserStatus));
    }

    [HttpPut("transfer-trigger")]
    public async Task<ActionResult<TransferTriggerResponse>> UpdateTransferTrigger(UpdateTransferTriggerRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request.InactivityDays is not 30 and not 60 and not 90 && request.InactivityMinutes is not 1)
        {
            return BadRequest(new { message = "Inactivity trigger must be 1 minute, 30 days, 60 days, or 90 days." });
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var plan = await GetOrCreateDefaultPlanAsync(userId.Value, cancellationToken);
        var schedule = await GetOrCreateTransferScheduleAsync(plan, user.LastLoginAt ?? user.CreatedAt, cancellationToken);
        if (schedule.IsConfigurationLocked)
        {
            return BadRequest(new { message = "Transfer trigger configuration is locked and cannot be changed." });
        }

        schedule.CheckIntervalDays = request.InactivityMinutes == 1 ? 30 : request.InactivityDays;
        schedule.CheckIntervalMinutes = request.InactivityMinutes == 1 ? 1 : 0;
        schedule.GracePeriodDays = 7;
        schedule.MaxFailedAttempts = 3;
        schedule.PreferredChannel = "Email";
        schedule.IsActive = true;
        schedule.IsConfigurationLocked = true;
        schedule.NextCheckAt = AddTriggerInterval(user.LastLoginAt ?? user.CreatedAt, schedule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToTransferTriggerResponse(schedule, user.UserStatus));
    }

    [HttpGet("proof-of-life/confirm/{checkinId:guid}")]
    [AllowAnonymous]
    public async Task<ContentResult> ConfirmAlive(Guid checkinId, CancellationToken cancellationToken)
    {
        var checkin = await _dbContext.ProofOfLifeCheckins
            .Include(item => item.Schedule)
                .ThenInclude(schedule => schedule.LegacyPlan)
                    .ThenInclude(plan => plan.OwnerUser)
            .FirstOrDefaultAsync(item => item.CheckinId == checkinId, cancellationToken);

        if (checkin is null)
        {
            return HtmlResult("Memoria proof-of-life", "This confirmation link is invalid or has expired.");
        }

        var now = DateTime.UtcNow;
        checkin.CheckinStatus = "ConfirmedAlive";
        checkin.RespondedAt = now;
        checkin.Schedule.NextCheckAt = AddTriggerInterval(now, checkin.Schedule);
        checkin.Schedule.LegacyPlan.OwnerUser.UserStatus = "Active";
        checkin.Schedule.LegacyPlan.OwnerUser.LastLoginAt = now;
        checkin.Schedule.LegacyPlan.OwnerUser.UpdatedAt = now;

        foreach (var pending in await _dbContext.ProofOfLifeCheckins
            .Where(item => item.ScheduleId == checkin.ScheduleId && item.CheckinStatus == "Sent")
            .ToListAsync(cancellationToken))
        {
            pending.CheckinStatus = "ConfirmedAlive";
            pending.RespondedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return HtmlResult("You are confirmed safe", "Thank you. Memoria has confirmed that you are alive and your legacy transfer will not be released.");
    }

    private async Task<LegacyPlan> GetOrCreateDefaultPlanAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.LegacyPlans
            .OrderByDescending(item => item.IsEcontractSigned)
            .ThenBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(item => item.OwnerUserId == userId, cancellationToken);

        if (plan is not null)
        {
            return plan;
        }

        plan = new LegacyPlan
        {
            LegacyPlanId = Guid.NewGuid(),
            OwnerUserId = userId,
            PlanName = "Default legacy transfer",
            IsEcontractSigned = false,
            PlanStatus = "Draft",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.LegacyPlans.Add(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return plan;
    }

    private async Task<ProofOfLifeSchedule> GetOrCreateTransferScheduleAsync(LegacyPlan plan, DateTime lastActivity, CancellationToken cancellationToken)
    {
        var schedule = await _dbContext.ProofOfLifeSchedules
            .FirstOrDefaultAsync(item => item.LegacyPlanId == plan.LegacyPlanId, cancellationToken);
        if (schedule is not null)
        {
            return schedule;
        }

        schedule = new ProofOfLifeSchedule
        {
            ScheduleId = Guid.NewGuid(),
            LegacyPlanId = plan.LegacyPlanId,
            CheckIntervalDays = 90,
            CheckIntervalMinutes = 0,
            GracePeriodDays = 7,
            MaxFailedAttempts = 3,
            PreferredChannel = "Email",
            IsActive = true,
            IsConfigurationLocked = false,
            NextCheckAt = lastActivity.AddDays(90),
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ProofOfLifeSchedules.Add(schedule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return schedule;
    }

    private string LoadContractText()
    {
        var candidates = new[]
        {
            Path.Combine(_environment.ContentRootPath, "Contracts", "hop-dong.docx"),
            Path.Combine(@"c:\Users\ASUS\Downloads", "h\u1ee3p \u0111\u1ed3ng.docx")
        };

        foreach (var path in candidates)
        {
            if (!System.IO.File.Exists(path))
            {
                continue;
            }

            try
            {
                return ExtractDocxText(path);
            }
            catch
            {
            }
        }

        return """
            Há»¢P Äá»’NG Dá»ŠCH Vá»¤ LÆ¯U TRá»® VÃ€ CHUYá»‚N GIAO DI Sáº¢N Ká»¸ THUáº¬T Sá»

            CÄƒn cá»© Bá»™ luáº­t DÃ¢n sá»± sá»‘ 91/2015/QH13 Ä‘Æ°á»£c Quá»‘c há»™i ban hÃ nh ngÃ y 24/11/2015;
            CÄƒn cá»© Luáº­t Giao dá»‹ch Ä‘iá»‡n tá»­ sá»‘ 20/2023/QH15 Ä‘Æ°á»£c Quá»‘c há»™i ban hÃ nh ngÃ y 22/06/2023;
            CÄƒn cá»© Nghá»‹ Ä‘á»‹nh 13/2023/NÄ-CP vá» báº£o vá»‡ dá»¯ liá»‡u cÃ¡ nhÃ¢n;

            BÃŠN B: KHÃCH HÃ€NG
            Há» vÃ  tÃªn:(ThÃ´ng tin hiá»ƒn thá»‹ dáº¡ng tÄ©nh Ä‘Æ°á»£c há»‡ thá»‘ng trÃ­ch xuáº¥t tá»± Ä‘á»™ng tá»« dá»¯ liá»‡u tÃ i khoáº£n xÃ¡c thá»±c cá»§a User)
            Sá»‘ CCCD/Há»™ chiáº¿u:(ThÃ´ng tin hiá»ƒn thá»‹ dáº¡ng tÄ©nh Ä‘Æ°á»£c há»‡ thá»‘ng trÃ­ch xuáº¥t tá»± Ä‘á»™ng tá»« dá»¯ liá»‡u tÃ i khoáº£n xÃ¡c thá»±c cá»§a User)
            NgÃ y cáº¥p:(ThÃ´ng tin hiá»ƒn thá»‹ dáº¡ng tÄ©nh Ä‘Æ°á»£c há»‡ thá»‘ng trÃ­ch xuáº¥t tá»± Ä‘á»™ng tá»« dá»¯ liá»‡u tÃ i khoáº£n xÃ¡c thá»±c cá»§a User)
            NÆ¡i cáº¥p:(ThÃ´ng tin hiá»ƒn thá»‹ dáº¡ng tÄ©nh Ä‘Æ°á»£c há»‡ thá»‘ng trÃ­ch xuáº¥t tá»± Ä‘á»™ng tá»« dá»¯ liá»‡u tÃ i khoáº£n xÃ¡c thá»±c cá»§a User)
            Sá»‘ Ä‘iá»‡n thoáº¡i Ä‘Äƒng kÃ½ tÃ i khoáº£n (SÄT nháº­n OTP):(ThÃ´ng tin hiá»ƒn thá»‹ dáº¡ng tÄ©nh Ä‘Æ°á»£c há»‡ thá»‘ng trÃ­ch xuáº¥t tá»± Ä‘á»™ng tá»« dá»¯ liá»‡u tÃ i khoáº£n xÃ¡c thá»±c cá»§a User)
            Email liÃªn há»‡ chÃ­nh thá»©c:(ThÃ´ng tin hiá»ƒn thá»‹ dáº¡ng tÄ©nh Ä‘Æ°á»£c há»‡ thá»‘ng trÃ­ch xuáº¥t tá»± Ä‘á»™ng tá»« dá»¯ liá»‡u tÃ i khoáº£n xÃ¡c thá»±c cá»§a User)

            ÄIá»€U 1: PHáº M VI Dá»ŠCH Vá»¤
            BÃªn A cung cáº¥p dá»‹ch vá»¥ lÆ°u trá»¯ dá»¯ liá»‡u sá»‘, cáº¥u hÃ¬nh ngÆ°á»i nháº­n di sáº£n vÃ  há»— trá»£ chuyá»ƒn giao di sáº£n ká»¹ thuáº­t sá»‘ khi cÃ¡c Ä‘iá»u kiá»‡n xÃ¡c minh Ä‘Æ°á»£c Ä‘Ã¡p á»©ng.

            ÄIá»€U 2: NGUYÃŠN Táº®C Báº¢O Máº¬T VÃ€ XÃC THá»°C
            Viá»‡c truy cáº­p, giáº£i mÃ£ vÃ  chuyá»ƒn giao di sáº£n chá»‰ Ä‘Æ°á»£c thá»±c hiá»‡n thÃ´ng qua cÆ¡ cháº¿ xÃ¡c thá»±c nhiá»u bÆ°á»›c, bao gá»“m thÃ´ng tin Ä‘á»‹nh danh, há»“ sÆ¡ phÃ¡p lÃ½ vÃ  mÃ£ OTP gá»­i tá»›i kÃªnh liÃªn há»‡ Ä‘Ã£ Ä‘Æ°á»£c chá»§ tÃ i khoáº£n thiáº¿t láº­p trÆ°á»›c.

            ÄIá»€U 3: QUYá»€N VÃ€ NGHÄ¨A Vá»¤ Cá»¦A CHá»¦ TÃ€I KHOáº¢N
            Chá»§ tÃ i khoáº£n chá»‹u trÃ¡ch nhiá»‡m cung cáº¥p thÃ´ng tin chÃ­nh xÃ¡c, cáº­p nháº­t ngÆ°á»i nháº­n di sáº£n vÃ  báº£o máº­t tÃ i khoáº£n, email, sá»‘ Ä‘iá»‡n thoáº¡i nháº­n OTP cá»§a mÃ¬nh.

            ÄIá»€U 4: Báº¢O Vá»† Dá»® LIá»†U CÃ NHÃ‚N
            BÃªn B Ä‘á»“ng Ã½ cho BÃªn A xá»­ lÃ½ dá»¯ liá»‡u cÃ¡ nhÃ¢n cáº§n thiáº¿t Ä‘á»ƒ váº­n hÃ nh dá»‹ch vá»¥, xÃ¡c minh yÃªu cáº§u nháº­n di sáº£n vÃ  thá»±c hiá»‡n nghÄ©a vá»¥ báº£o máº­t theo quy Ä‘á»‹nh phÃ¡p luáº­t.

            ÄIá»€U 5: Há»’ SÆ  PHÃP LÃ VÃ€ QUY TRÃŒNH XÃC THá»°C KÃCH HOáº T BIá»†N PHÃP THá»¦ CÃ”NG
            ThÃ´ng tin Ä‘á»‹nh danh cá»§a NgÆ°á»i nháº­n: Nháº­p Há» tÃªn vÃ  Sá»‘ CCCD vÃ o Ã´ input trÃªn há»‡ thá»‘ng Ä‘á»ƒ BÃªn A Ä‘á»‘i chiáº¿u xem cÃ³ trÃ¹ng khá»›p vá»›i thÃ´ng tin mÃ  Chá»§ tÃ i khoáº£n Ä‘Ã£ cÃ i Ä‘áº·t sáºµn ban Ä‘áº§u hay khÃ´ng.
            Há»“ sÆ¡ chá»©ng minh Sá»± kiá»‡n tá»­ vong: Sá»­ dá»¥ng nÃºt upload trÃªn há»‡ thá»‘ng Ä‘á»ƒ táº£i lÃªn file áº£nh hoáº·c file PDF cá»§a Giáº¥y chá»©ng tá»­; hoáº·c Quyáº¿t Ä‘á»‹nh tuyÃªn bá»‘ má»™t ngÆ°á»i Ä‘Ã£ cháº¿t cá»§a TÃ²a Ã¡n.
            XÃ¡c thá»±c qua mÃ£ OTP: Há»‡ thá»‘ng sáº½ gá»­i má»™t mÃ£ OTP vá» sá»‘ Ä‘iá»‡n thoáº¡i hoáº·c email cá»§a chÃ­nh NgÆ°á»i nháº­n di sáº£n, lÃ  thÃ´ng tin liÃªn láº¡c Ä‘Ã£ Ä‘Æ°á»£c Chá»§ tÃ i khoáº£n gÃ¡n cho há» tá»« trÆ°á»›c. NgÆ°á»i nháº­n di sáº£n pháº£i nháº­p Ä‘Ãºng mÃ£ OTP nÃ y Ä‘á»ƒ xÃ¡c nháº­n quyá»n thá»±c hiá»‡n yÃªu cáº§u.

            ÄIá»€U 6: QUY TRÃŒNH KHÃNG NGHá»Š "BÃO Äá»˜NG GIáº¢"
            Náº¿u Chá»§ tÃ i khoáº£n Ä‘Äƒng nháº­p láº¡i vÃ  xÃ¡c thá»±c OTP thÃ nh cÃ´ng, há»‡ thá»‘ng há»§y tiáº¿n trÃ¬nh nghi váº¥n váº¯ng máº·t vÃ  Ä‘Æ°a tÃ i khoáº£n vá» tráº¡ng thÃ¡i hoáº¡t Ä‘á»™ng bÃ¬nh thÆ°á»ng.

            ÄIá»€U 7: GIá»šI Háº N TRÃCH NHIá»†M DO Sá»° Cá» CÃ”NG NGHá»† VÃ€ Lá»–I Báº¢O Máº¬T USER
            BÃªn A Ä‘Æ°á»£c miá»…n trá»« trÃ¡ch nhiá»‡m Ä‘á»‘i vá»›i cÃ¡c thiá»‡t háº¡i phÃ¡t sinh tá»« sá»± kiá»‡n báº¥t kháº£ khÃ¡ng, rÃ² rá»‰ thÃ´ng tin do BÃªn B Ä‘á»ƒ lá»™ tÃ i khoáº£n, máº­t kháº©u, email hoáº·c mÃ£ OTP.

            ÄIá»€U 8: GIÃ TRá»Š PHÃP LÃ VÃ€ PHÆ¯Æ NG THá»¨C GIAO Káº¾T Há»¢P Äá»’NG ÄIá»†N Tá»¬
            Há»£p Ä‘á»“ng nÃ y Ä‘Æ°á»£c giao káº¿t Ä‘iá»‡n tá»­ trÃªn cÆ¡ sá»Ÿ tá»± nguyá»‡n. BÃªn B xÃ¡c nháº­n Ä‘Ã£ Ä‘á»c, hiá»ƒu rÃµ vÃ  Ä‘á»“ng Ã½ vá»›i toÃ n bá»™ Ä‘iá»u khoáº£n cá»§a Há»£p Ä‘á»“ng dá»‹ch vá»¥ lÆ°u trá»¯ vÃ  chuyá»ƒn giao di sáº£n ká»¹ thuáº­t sá»‘.
            """;
    }
    private static string ExtractDocxText(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidOperationException("document.xml was not found.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        return string.Join(Environment.NewLine, document
            .Descendants(w + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(w + "t").Select(text => text.Value)).Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string RewriteArticleFive(string contractText)
    {
        var lines = contractText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var startIndex = lines.FindIndex(line => line.StartsWith("\u0110I\u1ec0U 5:", StringComparison.OrdinalIgnoreCase));
        var endIndex = lines.FindIndex(line => line.StartsWith("\u0110I\u1ec0U 6:", StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0 || endIndex <= startIndex)
        {
            return contractText;
        }

        var replacement = new[]
        {
            "ĐIỀU 5: HỒ SƠ PHÁP LÝ VÀ QUY TRÌNH XÁC THỰC KÍCH HOẠT BIỆN PHÁP THỦ CÔNG",
            "Để bảo mật thông tin, ngăn chặn các hành vi trục lợi hoặc làm giả thông tin nhằm chiếm đoạt dữ liệu của khách hàng, khi nhận được thông báo từ hệ thống, Người nhận di sản cần thực hiện các bước xác thực sau thông qua giao diện điện tử của Bên A:",
            "Thông tin định danh của Người nhận: Nhập Họ tên và Số CCCD vào ô input trên hệ thống để Bên A đối chiếu xem có trùng khớp với thông tin mà Chủ tài khoản đã cài đặt sẵn ban đầu hay không.",
            "Hồ sơ chứng minh Sự kiện tử vong: Sử dụng nút upload trên hệ thống để tải lên file ảnh hoặc file PDF của Giấy chứng tử do cơ quan Nhà nước có thẩm quyền cấp; hoặc Quyết định tuyên bố một người đã chết của Tòa án.",
            "Xác thực qua mã OTP: Hệ thống sẽ gửi một mã OTP về số điện thoại hoặc email của chính Người nhận di sản, là thông tin liên lạc đã được Chủ tài khoản gán cho họ từ trước. Người nhận di sản phải nhập đúng mã OTP này để xác nhận quyền thực hiện yêu cầu.",
            "Quyền từ chối của Bên A: Trong trường hợp thông tin định danh không trùng khớp, hồ sơ tử vong không hợp lệ, file tải lên không đọc được hoặc mã OTP xác thực không chính xác, Bên A có quyền từ chối giải mã và chuyển giao di sản cho đến khi Người nhận di sản hoàn tất yêu cầu xác minh hợp lệ."
        };

        lines.RemoveRange(startIndex, endIndex - startIndex);
        lines.InsertRange(startIndex, replacement);
        return string.Join(Environment.NewLine, lines);
    }
    private static string ApplyUserInfo(string contractText, User user)
    {
        return contractText
            .Replace("Họ và tên:(Thông tin hiển thị dạng tĩnh được hệ thống trích xuất tự động từ dữ liệu tài khoản xác thực của User)", $"Họ và tên: {ValueOrMissing(user.FullName)}")
            .Replace("Số CCCD/Hộ chiếu:(Thông tin hiển thị dạng tĩnh được hệ thống trích xuất tự động từ dữ liệu tài khoản xác thực của User)", $"Số CCCD/Hộ chiếu: {ValueOrMissing(user.CccdNumber)}")
            .Replace("Ngày cấp:(Thông tin hiển thị dạng tĩnh được hệ thống trích xuất tự động từ dữ liệu tài khoản xác thực của User)", $"Ngày cấp: {ValueOrMissing(user.CccdIssuedDate?.ToString("yyyy-MM-dd"))}")
            .Replace("Nơi cấp:(Thông tin hiển thị dạng tĩnh được hệ thống trích xuất tự động từ dữ liệu tài khoản xác thực của User)", $"Nơi cấp: {ValueOrMissing(user.CccdIssuedPlace)}")
            .Replace("Số điện thoại đăng ký tài khoản (SĐT nhận OTP):(Thông tin hiển thị dạng tĩnh được hệ thống trích xuất tự động từ dữ liệu tài khoản xác thực của User)", $"Số điện thoại đăng ký tài khoản (SĐT nhận OTP): {ValueOrMissing(user.PhoneNumber)}")
            .Replace("Email liên hệ chính thức:(Thông tin hiển thị dạng tĩnh được hệ thống trích xuất tự động từ dữ liệu tài khoản xác thực của User)", $"Email liên hệ chính thức: {ValueOrMissing(user.Email)}");
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

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(authorization["Bearer ".Length..].Trim());
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

    private static string ValueOrMissing(string? value) => string.IsNullOrWhiteSpace(value) ? "ChÆ°a cáº­p nháº­t" : value.Trim();

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@', 2);
        return parts.Length != 2 || parts[0].Length <= 2 ? email : $"{parts[0][0]}***{parts[0][^1]}@{parts[1]}";
    }

    private static string MaskIdentityDocument(string value)
    {
        var compact = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length <= 4)
        {
            return compact;
        }

        return $"{new string('*', Math.Max(0, compact.Length - 4))}{compact[^4..]}";
    }

    private static string HashIdentityDocument(string value)
    {
        var normalized = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static LegacyBeneficiaryResponse ToBeneficiaryResponse(Beneficiary beneficiary) => new(
        beneficiary.BeneficiaryId,
        beneficiary.FullName,
        beneficiary.Email,
        beneficiary.PhoneNumber,
        beneficiary.Relationship,
        beneficiary.IdentityDocumentMasked,
        beneficiary.IsPrimary,
        beneficiary.CreatedAt);

    private static LegacyStoredFileResponse ToStoredFileResponse(StoredFile file) => new(
        file.FileId,
        file.OriginalFileName,
        file.FileUrl,
        file.MimeType,
        file.FileSizeBytes,
        file.CreatedAt);

    private static TransferTriggerResponse ToTransferTriggerResponse(ProofOfLifeSchedule schedule, string userStatus) => new(
        schedule.CheckIntervalDays,
        schedule.CheckIntervalMinutes,
        schedule.NextCheckAt,
        schedule.GracePeriodDays,
        schedule.MaxFailedAttempts,
        userStatus,
        schedule.IsConfigurationLocked);

    private static DateTime AddTriggerInterval(DateTime from, ProofOfLifeSchedule schedule) =>
        schedule.CheckIntervalMinutes > 0
            ? from.AddMinutes(schedule.CheckIntervalMinutes)
            : from.AddDays(schedule.CheckIntervalDays);

    private static ContentResult HtmlResult(string title, string message)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        var safeMessage = System.Net.WebUtility.HtmlEncode(message);
        return new ContentResult
        {
            ContentType = "text/html; charset=utf-8",
            Content = $$"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>{{safeTitle}}</title>
                  <style>
                    body { margin:0; min-height:100vh; display:grid; place-items:center; font-family:Arial,sans-serif; background:#fbf9f1; color:#1b1c17; }
                    main { max-width:560px; margin:24px; padding:32px; border-radius:28px; background:#fff; box-shadow:0 24px 70px -32px rgba(112,88,91,.45); }
                    h1 { margin:0 0 12px; color:#70585b; }
                    p { margin:0; line-height:1.6; color:#4f4445; font-weight:600; }
                  </style>
                </head>
                <body><main><h1>{{safeTitle}}</h1><p>{{safeMessage}}</p></main></body>
                </html>
                """
        };
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = System.IO.File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void TryDeleteLocalStoredFile(StoredFile file)
    {
        try
        {
            var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var uri = new Uri(file.FileUrl);
            var localUploadsIndex = uri.AbsolutePath.IndexOf("/local-uploads/", StringComparison.OrdinalIgnoreCase);
            if (localUploadsIndex < 0)
            {
                return;
            }

            var relative = Uri.UnescapeDataString(uri.AbsolutePath[(localUploadsIndex + 1)..]).Replace('/', Path.DirectorySeparatorChar);
            var absolutePath = Path.GetFullPath(Path.Combine(root, relative));
            var uploadsRoot = Path.GetFullPath(Path.Combine(root, "local-uploads"));
            if (absolutePath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(absolutePath))
            {
                System.IO.File.Delete(absolutePath);
            }
        }
        catch
        {
        }
    }
}

public sealed record LegacyContractResponse(Guid LegacyPlanId, bool IsSigned, DateTime? SignedAt, string ContractText);

public sealed record LegacyContractOtpResponse(Guid? VerificationId, string Email, DateTime? ExpiresAt);

public sealed record VerifyLegacyContractOtpRequest(Guid VerificationId, string Code);

public sealed record LegacyContractSignedResponse(bool IsSigned, DateTime SignedAt);

public sealed record CreateLegacyBeneficiaryRequest(
    string? FullName,
    string? Email,
    string? PhoneNumber,
    string? Relationship,
    string? IdentityDocumentNumber,
    bool IsPrimary);

public sealed record LegacyBeneficiaryResponse(
    Guid BeneficiaryId,
    string FullName,
    string? Email,
    string? PhoneNumber,
    string Relationship,
    string? IdentityDocumentMasked,
    bool IsPrimary,
    DateTime CreatedAt);

public sealed record LegacyStoredFileResponse(
    Guid FileId,
    string OriginalFileName,
    string FileUrl,
    string MimeType,
    long FileSizeBytes,
    DateTime CreatedAt);

public sealed record StorageBucketDefinition(
    string Key,
    string DisplayName,
    HashSet<string> AllowedExtensions,
    HashSet<string> AllowedMimeTypes);

public sealed record UpdateTransferTriggerRequest(int InactivityDays, int? InactivityMinutes);

public sealed record TransferTriggerResponse(
    int InactivityDays,
    int InactivityMinutes,
    DateTime NextCheckAt,
    int PingIntervalDays,
    int MaxPingAttempts,
    string UserStatus,
    bool IsLocked);


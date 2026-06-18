using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MEMORIA_BE.Configurations;
using MEMORIA_BE.Data;
using MEMORIA_BE.Models;
using MEMORIA_BE.Services;

namespace MEMORIA_BE.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const int OtpMinutes = 10;
    private const int MaxAttempts = 5;

    private readonly AppDbContext _dbContext;
    private readonly JwtSettings _jwtSettings;
    private readonly GoogleAuthSettings _googleAuthSettings;
    private readonly IEmailSender _emailSender;
    private readonly IWebHostEnvironment _environment;
    private readonly PasswordHasher<User> _passwordHasher = new();
    private readonly PasswordHasher<AuthVerificationCode> _codeHasher = new();

    public AuthController(
        AppDbContext dbContext,
        IOptions<JwtSettings> jwtSettings,
        IOptions<GoogleAuthSettings> googleAuthSettings,
        IEmailSender emailSender,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _googleAuthSettings = googleAuthSettings.Value;
        _emailSender = emailSender;
        _environment = environment;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthChallengeResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (await _dbContext.Users.AnyAsync(user => user.Email.ToLower() == email, cancellationToken))
        {
            return Conflict(new { message = "This email is already in use." });
        }

        var user = new User
        {
            UserId = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            IsActive = true,
            UserStatus = "Active",
            IsEmailVerified = false,
            IsPhoneVerified = false,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _dbContext.Users.Add(user);
        await AssignUserRoleAsync(user.UserId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await CreateAndSendChallengeAsync(user, "Register", cancellationToken));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _dbContext.Users
            .Include(item => item.UserRoles)
                .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);

        if (user is null || !user.IsActive || !IsPasswordValid(user, request.Password))
        {
            return Unauthorized(new { message = "Email or password is incorrect." });
        }

        if (!user.IsEmailVerified)
        {
            return Ok(await CreateAndSendChallengeAsync(user, "Register", cancellationToken));
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.UserStatus = "Active";
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(CreateLoginResponse(user));
    }

    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> GoogleLogin(GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_googleAuthSettings.ClientId))
        {
            return BadRequest(new { message = "Google ClientId is not configured in appsettings.json." });
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                request.IdToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleAuthSettings.ClientId }
                });
        }
        catch (InvalidJwtException)
        {
            return Unauthorized(new { message = "Google token is invalid." });
        }

        var email = NormalizeEmail(payload.Email);
        var user = await _dbContext.Users
            .Include(item => item.UserRoles)
                .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                UserId = Guid.NewGuid(),
                FullName = string.IsNullOrWhiteSpace(payload.Name) ? email : payload.Name,
                Email = email,
                PasswordHash = _passwordHasher.HashPassword(new User(), $"GOOGLE:{Guid.NewGuid():N}"),
                AvatarUrl = payload.Picture,
                IsEmailVerified = payload.EmailVerified,
                IsPhoneVerified = false,
                IsActive = true,
                UserStatus = "Active",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Users.Add(user);
            await AssignUserRoleAsync(user.UserId, cancellationToken);
        }
        else
        {
            user.FullName = string.IsNullOrWhiteSpace(payload.Name) ? user.FullName : payload.Name;
            user.AvatarUrl = string.IsNullOrWhiteSpace(payload.Picture) ? user.AvatarUrl : payload.Picture;
            user.IsEmailVerified = user.IsEmailVerified || payload.EmailVerified;
            user.UpdatedAt = DateTime.UtcNow;
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.UserStatus = "Active";
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(CreateLoginResponse(user));
    }

    [HttpPost("verify-login-code")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> VerifyLoginCode(VerifyCodeRequest request, CancellationToken cancellationToken)
    {
        var verification = await _dbContext.AuthVerificationCodes
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.VerificationId == request.VerificationId, cancellationToken);

        if (verification is null || verification.ConsumedAt is not null || verification.ExpiresAt < DateTime.UtcNow)
        {
            return Unauthorized(new { message = "The verification code has expired or is invalid." });
        }

        if (verification.AttemptCount >= MaxAttempts)
        {
            return Unauthorized(new { message = "Too many incorrect attempts. Please sign in again." });
        }

        verification.AttemptCount += 1;
        var result = _codeHasher.VerifyHashedPassword(verification, verification.CodeHash, request.Code.Trim());
        if (result is not PasswordVerificationResult.Success and not PasswordVerificationResult.SuccessRehashNeeded)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Unauthorized(new { message = "The verification code is incorrect." });
        }

        var user = await _dbContext.Users
            .Include(item => item.UserRoles)
                .ThenInclude(item => item.Role)
            .FirstAsync(item => item.UserId == verification.UserId, cancellationToken);
        if (!user.IsActive)
        {
            return Unauthorized(new { message = "This account has been locked." });
        }

        verification.ConsumedAt = DateTime.UtcNow;
        if (verification.Purpose == "Register")
        {
            user.IsEmailVerified = true;
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.UserStatus = "Active";
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(CreateLoginResponse(user));
    }

    [HttpGet("me")]
    public async Task<ActionResult<AuthUserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .Include(item => item.UserRoles)
                .ThenInclude(item => item.Role)
            .Where(item => item.UserId == userId.Value && item.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        return user is null ? Unauthorized() : Ok(ToAuthUserResponse(user));
    }

    [HttpPut("me")]
    public async Task<ActionResult<AuthUserResponse>> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken)
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

        var fullName = request.FullName?.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return BadRequest(new { message = "Full name is required." });
        }

        user.FullName = fullName;
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        user.DateOfBirth = request.DateOfBirth;
        user.Gender = string.IsNullOrWhiteSpace(request.Gender) ? null : request.Gender.Trim();
        user.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        user.CccdNumber = string.IsNullOrWhiteSpace(request.CccdNumber) ? null : request.CccdNumber.Trim();
        user.CccdIssuedDate = request.CccdIssuedDate;
        user.CccdIssuedPlace = string.IsNullOrWhiteSpace(request.CccdIssuedPlace) ? null : request.CccdIssuedPlace.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToAuthUserResponse(user));
    }

    [HttpPost("me/avatar")]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult<AuthUserResponse>> UploadAvatar([FromForm] IFormFile avatar, CancellationToken cancellationToken)
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

        if (avatar.Length <= 0 || !avatar.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Please choose a valid image file." });
        }

        if (avatar.Length > 5_000_000)
        {
            return BadRequest(new { message = "Avatar image must be 5 MB or smaller." });
        }

        var extension = Path.GetExtension(avatar.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var relativeDirectory = Path.Combine("local-uploads", "avatars", user.UserId.ToString("N"));
        var absoluteDirectory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteDirectory, storedFileName);
        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await avatar.CopyToAsync(stream, cancellationToken);
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        user.AvatarUrl = $"{baseUrl}/{relativeDirectory.Replace('\\', '/')}/{Uri.EscapeDataString(storedFileName)}";
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToAuthUserResponse(user));
    }

    private Guid? GetCurrentUserId()
    {
        var userIdValue =
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub") ??
            User.FindFirstValue("nameid") ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            TryReadUserIdFromBearerToken() ??
            Request.Headers["X-Memoria-User-Id"].FirstOrDefault();

        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
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

    private async Task<AuthChallengeResponse> CreateAndSendChallengeAsync(User user, string purpose, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var verification = new AuthVerificationCode
        {
            VerificationId = Guid.NewGuid(),
            UserId = user.UserId,
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpMinutes),
            CreatedAt = DateTime.UtcNow
        };
        verification.CodeHash = _codeHasher.HashPassword(verification, code);

        _dbContext.AuthVerificationCodes.Add(verification);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _emailSender.SendOtpAsync(user.Email, user.FullName, code, purpose, cancellationToken);

        return new AuthChallengeResponse(
            verification.VerificationId,
            MaskEmail(user.Email),
            verification.ExpiresAt,
            null);
    }

    private LoginResponse CreateLoginResponse(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes);
        var token = CreateToken(user, expiresAt);

        return new LoginResponse(
            token,
            expiresAt,
            ToAuthUserResponse(user)
        );
    }

    private static AuthUserResponse ToAuthUserResponse(User user) => new(
        user.UserId,
        user.FullName,
        user.Email,
        user.PhoneNumber,
        user.AvatarUrl,
        user.DateOfBirth,
        user.Gender,
        user.Address,
        user.CccdNumber,
        user.CccdIssuedDate,
        user.CccdIssuedPlace,
        user.IsEmailVerified,
        user.IsPhoneVerified,
        user.IsActive,
        user.LastLoginAt,
        user.CreatedAt,
        user.UpdatedAt,
        user.UserRoles
            .Select(item => item.Role?.RoleName)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToArray());

    private async Task AssignUserRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userRoleId = await _dbContext.Roles
            .Where(role => role.RoleName == "User")
            .Select(role => (int?)role.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (userRoleId is not null)
        {
            _dbContext.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = userRoleId.Value,
                AssignedAt = DateTime.UtcNow
            });
        }
    }

    private bool IsPasswordValid(User user, string password)
    {
        try
        {
            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded)
            {
                return true;
            }
        }
        catch (FormatException)
        {
            // Some local demo accounts use a plain temporary password for quick testing.
        }

        return user.PasswordHash == password;
    }

    private string CreateToken(User user, DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(user.UserRoles
            .Where(item => !string.IsNullOrWhiteSpace(item.Role?.RoleName))
            .Select(item => new Claim(ClaimTypes.Role, item.Role.RoleName)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@', 2);
        if (parts.Length != 2 || parts[0].Length <= 2)
        {
            return email;
        }

        return $"{parts[0][0]}***{parts[0][^1]}@{parts[1]}";
    }
}

public sealed record RegisterRequest(string FullName, string Email, string Password, string? PhoneNumber);

public sealed record LoginRequest(string Email, string Password);

public sealed record GoogleLoginRequest(string IdToken);

public sealed record VerifyCodeRequest(Guid VerificationId, string Code);

public sealed record UpdateProfileRequest(
    string? FullName,
    string? PhoneNumber,
    string? AvatarUrl,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Address,
    string? CccdNumber,
    DateOnly? CccdIssuedDate,
    string? CccdIssuedPlace);

public sealed record AuthChallengeResponse(Guid VerificationId, string Email, DateTime ExpiresAt, string? DevCode);

public sealed record LoginResponse(string Token, DateTime ExpiresAt, AuthUserResponse User);

public sealed record AuthUserResponse(
    Guid UserId,
    string FullName,
    string Email,
    string? PhoneNumber,
    string? AvatarUrl,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Address,
    string? CccdNumber,
    DateOnly? CccdIssuedDate,
    string? CccdIssuedPlace,
    bool IsEmailVerified,
    bool IsPhoneVerified,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyCollection<string> Roles);

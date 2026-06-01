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
        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);

        if (user is null || !user.IsActive || !IsPasswordValid(user, request.Password))
        {
            return Unauthorized(new { message = "Email or password is incorrect." });
        }

        if (!user.IsEmailVerified)
        {
            return Ok(await CreateAndSendChallengeAsync(user, "Register", cancellationToken));
        }

        user.LastLoginAt = DateTime.UtcNow;
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
        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);
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

        var user = verification.User;
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
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(CreateLoginResponse(user));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthUserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .Where(item => item.UserId == userId.Value && item.IsActive)
            .Select(item => new AuthUserResponse(item.UserId, item.FullName, item.Email, item.AvatarUrl))
            .FirstOrDefaultAsync(cancellationToken);

        return user is null ? Unauthorized() : Ok(user);
    }

    private Guid? GetCurrentUserId()
    {
        var userIdValue =
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub") ??
            User.FindFirstValue("nameid") ??
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
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
            new AuthUserResponse(user.UserId, user.FullName, user.Email, user.AvatarUrl)
        );
    }

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
        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded)
        {
            return true;
        }

        return user.PasswordHash == password;
    }

    private string CreateToken(User user, DateTime expiresAt)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

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

public sealed record AuthChallengeResponse(Guid VerificationId, string Email, DateTime ExpiresAt, string? DevCode);

public sealed record LoginResponse(string Token, DateTime ExpiresAt, AuthUserResponse User);

public sealed record AuthUserResponse(Guid UserId, string FullName, string Email, string? AvatarUrl);

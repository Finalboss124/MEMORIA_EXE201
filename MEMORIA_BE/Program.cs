using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MEMORIA_BE.Configurations;
using MEMORIA_BE.Data;
using MEMORIA_BE.Middlewares;
using MEMORIA_BE.Services;

var builder = WebApplication.CreateBuilder(args);
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500_000_000;
});

builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500_000_000;
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("MemoriaUi", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "MEMORIA_BE API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your token."
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<GoogleAuthSettings>(builder.Configuration.GetSection("GoogleAuth"));
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("Cloudinary"));
builder.Services.Configure<FutureLetterDeliverySettings>(builder.Configuration.GetSection("FutureLetterDelivery"));
builder.Services.Configure<FutureLetterEncryptionSettings>(builder.Configuration.GetSection("FutureLetterEncryption"));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<IFutureLetterCrypto, FutureLetterCrypto>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<ICloudFileStorage, CloudinaryFileStorage>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddHostedService<FutureLetterDeliveryWorker>();
builder.Services.AddHostedService<LegacyTransferTriggerWorker>();
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();
if (jwtSettings is null || string.IsNullOrWhiteSpace(jwtSettings.Key))
{
    throw new InvalidOperationException("JWT settings are missing or invalid.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
        ClockSkew = TimeSpan.FromMinutes(2)
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await ApplySchemaPatchesAsync(dbContext);
}

app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("MemoriaUi");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/local-uploads/{**filePath}", (string filePath, IWebHostEnvironment environment) =>
{
    var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    var uploadsRoot = Path.GetFullPath(Path.Combine(root, "local-uploads"));
    var requestedPath = Path.GetFullPath(Path.Combine(uploadsRoot, filePath ?? string.Empty));

    if (!requestedPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(requestedPath))
    {
        return Results.NotFound();
    }

    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(requestedPath, out var contentType))
    {
        contentType = "application/octet-stream";
    }

    return Results.File(requestedPath, contentType, enableRangeProcessing: true);
});

app.MapControllers();

app.Run();

static async Task ApplySchemaPatchesAsync(AppDbContext dbContext)
{
    await dbContext.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH('Users', 'CccdNumber') IS NULL
            ALTER TABLE Users ADD CccdNumber NVARCHAR(30) NULL;

        IF COL_LENGTH('Users', 'CccdIssuedDate') IS NULL
            ALTER TABLE Users ADD CccdIssuedDate DATE NULL;

        IF COL_LENGTH('Users', 'CccdIssuedPlace') IS NULL
            ALTER TABLE Users ADD CccdIssuedPlace NVARCHAR(255) NULL;

        IF COL_LENGTH('StoredFiles', 'StoragePurpose') IS NULL
            ALTER TABLE StoredFiles ADD StoragePurpose NVARCHAR(50) NULL;

        IF COL_LENGTH('Users', 'UserStatus') IS NULL
            ALTER TABLE Users ADD UserStatus NVARCHAR(40) NOT NULL CONSTRAINT DF_Users_UserStatus DEFAULT 'Active';

        IF COL_LENGTH('ProofOfLifeSchedules', 'IsConfigurationLocked') IS NULL
            ALTER TABLE ProofOfLifeSchedules ADD IsConfigurationLocked BIT NOT NULL CONSTRAINT DF_POLSchedules_IsConfigurationLocked DEFAULT 0;

        IF COL_LENGTH('ProofOfLifeSchedules', 'CheckIntervalMinutes') IS NULL
            ALTER TABLE ProofOfLifeSchedules ADD CheckIntervalMinutes INT NOT NULL CONSTRAINT DF_POLSchedules_CheckIntervalMinutes DEFAULT 0;

        IF COL_LENGTH('Beneficiaries', 'IdentityDocumentHash') IS NULL
            ALTER TABLE Beneficiaries ADD IdentityDocumentHash NVARCHAR(128) NULL;

        IF COL_LENGTH('LegacyUnlockRequests', 'ClaimTokenHash') IS NULL
            ALTER TABLE LegacyUnlockRequests ADD ClaimTokenHash NVARCHAR(128) NULL;

        IF COL_LENGTH('LegacyUnlockRequests', 'ClaimTokenExpiresAt') IS NULL
            ALTER TABLE LegacyUnlockRequests ADD ClaimTokenExpiresAt DATETIME2 NULL;

        IF COL_LENGTH('LegacyUnlockRequests', 'BeneficiaryNotifiedAt') IS NULL
            ALTER TABLE LegacyUnlockRequests ADD BeneficiaryNotifiedAt DATETIME2 NULL;

        IF COL_LENGTH('LegacyUnlockRequests', 'BeneficiaryVerifiedAt') IS NULL
            ALTER TABLE LegacyUnlockRequests ADD BeneficiaryVerifiedAt DATETIME2 NULL;

        IF COL_LENGTH('MemoryComments', 'ParentCommentId') IS NULL
            ALTER TABLE MemoryComments ADD ParentCommentId UNIQUEIDENTIFIER NULL;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.foreign_keys
            WHERE name = 'FK_MemoryComments_Parent'
              AND parent_object_id = OBJECT_ID('MemoryComments')
        )
            ALTER TABLE MemoryComments
                ADD CONSTRAINT FK_MemoryComments_Parent
                FOREIGN KEY (ParentCommentId) REFERENCES MemoryComments(CommentId);

        IF OBJECT_ID('MemoryLikes', 'U') IS NULL
        BEGIN
            CREATE TABLE MemoryLikes
            (
                MemoryLikeId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_MemoryLikes PRIMARY KEY DEFAULT NEWID(),
                MemoryId UNIQUEIDENTIFIER NOT NULL,
                UserId UNIQUEIDENTIFIER NOT NULL,
                CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_MemoryLikes_CreatedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_MemoryLikes_Memories FOREIGN KEY (MemoryId) REFERENCES Memories(MemoryId) ON DELETE CASCADE,
                CONSTRAINT FK_MemoryLikes_Users FOREIGN KEY (UserId) REFERENCES Users(UserId)
            );

            CREATE UNIQUE INDEX IX_MemoryLikes_Memory_User ON MemoryLikes(MemoryId, UserId);
        END;

        IF EXISTS (
            SELECT 1
            FROM sys.check_constraints
            WHERE name = 'CK_AuthVerificationCodes_Purpose'
              AND parent_object_id = OBJECT_ID('AuthVerificationCodes')
        )
            ALTER TABLE AuthVerificationCodes DROP CONSTRAINT CK_AuthVerificationCodes_Purpose;

        ALTER TABLE AuthVerificationCodes
            ADD CONSTRAINT CK_AuthVerificationCodes_Purpose
            CHECK (Purpose IN ('Login','Register','GoogleLogin','LegacyContract','LegacyClaimOtp'));

        IF EXISTS (
            SELECT 1
            FROM sys.check_constraints
            WHERE name = 'CK_LegalDoc_Type'
              AND parent_object_id = OBJECT_ID('LegalDocumentSubmissions')
        )
            ALTER TABLE LegalDocumentSubmissions DROP CONSTRAINT CK_LegalDoc_Type;

        ALTER TABLE LegalDocumentSubmissions
            ADD CONSTRAINT CK_LegalDoc_Type
            CHECK (DocumentType IN ('DeathCertificate','MissingPersonCourtDecision','BeneficiaryIdentityDocument'));
        """);
}

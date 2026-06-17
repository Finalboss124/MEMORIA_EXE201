using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MEMORIA_BE.Data;
using MEMORIA_BE.Models;
using MEMORIA_BE.Services;

namespace MEMORIA_BE.Controllers;

[ApiController]
[Route("api/family-vault")]
public sealed class FamilyVaultController : ControllerBase
{
    private const long MaxPostFileBytes = 80 * 1024 * 1024;
    private static readonly HashSet<string> AllowedPostMimePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/",
        "video/"
    };

    private readonly AppDbContext _dbContext;
    private readonly ICloudFileStorage _fileStorage;

    public FamilyVaultController(AppDbContext dbContext, ICloudFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    [HttpGet]
    public async Task<ActionResult<FamilyVaultResponse>> Get(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var vault = await GetCurrentDisplayVaultAsync(user, cancellationToken);
        if (vault is null)
        {
            vault = await GetOrCreateOwnedVaultAsync(user, cancellationToken);
        }
        return Ok(await ToVaultResponseAsync(vault.VaultId, user.UserId, cancellationToken));
    }

    [HttpGet("invitations")]
    public async Task<ActionResult<IReadOnlyList<FamilyVaultInvitationResponse>>> GetInvitations(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var email = NormalizeEmail(user.Email);
        var invitations = await _dbContext.FamilyVaultMembers
            .AsNoTracking()
            .Include(item => item.Vault)
                .ThenInclude(vault => vault.OwnerUser)
            .Where(item =>
                item.InviteStatus == "Pending" &&
                (item.UserId == userId.Value || (item.InviteEmail != null && item.InviteEmail.ToLower() == email)))
            .OrderByDescending(item => item.InvitedAt)
            .Select(item => new FamilyVaultInvitationResponse(
                item.VaultMemberId,
                item.VaultId,
                item.Vault.VaultName,
                item.Vault.OwnerUser.FullName,
                item.InvitedAt))
            .ToListAsync(cancellationToken);

        return Ok(invitations);
    }

    [HttpPost("invite")]
    public async Task<ActionResult<FamilyVaultMemberResponse>> Invite([FromBody] InviteFamilyMemberRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var owner = await _dbContext.Users.FirstOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (owner is null)
        {
            return Unauthorized();
        }

        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { message = "Enter the email of a Memoria user." });
        }

        if (!IsValidEmail(email))
        {
            return BadRequest(new { message = "Enter a valid member email address." });
        }

        var targetUser = await _dbContext.Users
            .FirstOrDefaultAsync(item => item.Email.ToLower() == email && item.IsActive, cancellationToken);
        if (targetUser is null)
        {
            return BadRequest(new { message = "No active Memoria account was found with that email." });
        }

        if (targetUser.UserId == owner.UserId)
        {
            return BadRequest(new { message = "You are already the owner of this vault." });
        }

        var vault = await GetOrCreateOwnedVaultAsync(owner, cancellationToken);
        var existing = await _dbContext.FamilyVaultMembers
            .FirstOrDefaultAsync(item =>
                item.VaultId == vault.VaultId &&
                (item.UserId == targetUser.UserId || (item.InviteEmail != null && item.InviteEmail.ToLower() == email)),
                cancellationToken);

        if (existing is not null)
        {
            if (existing.InviteStatus == "Rejected")
            {
                existing.UserId = targetUser.UserId;
                existing.InviteEmail = targetUser.Email;
                existing.MemberName = targetUser.FullName;
                existing.MemberRole = "Contributor";
                existing.InviteStatus = "Pending";
                existing.InvitedAt = DateTime.UtcNow;
                existing.AcceptedAt = null;

                _dbContext.NotificationLogs.Add(new NotificationLog
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = targetUser.UserId,
                    RecipientEmail = targetUser.Email,
                    Channel = "App",
                    Subject = $"FamilyVaultInvite:{existing.VaultMemberId}",
                    Message = $"{owner.FullName} invited you to join {vault.VaultName}.",
                    SendStatus = "Pending",
                    CreatedAt = DateTime.UtcNow
                });

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return Ok(ToMemberResponse(existing));
        }

        var now = DateTime.UtcNow;
        var member = new FamilyVaultMember
        {
            VaultMemberId = Guid.NewGuid(),
            VaultId = vault.VaultId,
            UserId = targetUser.UserId,
            InviteEmail = targetUser.Email,
            MemberName = targetUser.FullName,
            MemberRole = "Contributor",
            InviteStatus = "Pending",
            InvitedAt = now
        };

        _dbContext.FamilyVaultMembers.Add(member);
        _dbContext.NotificationLogs.Add(new NotificationLog
        {
            NotificationId = Guid.NewGuid(),
            UserId = targetUser.UserId,
            RecipientEmail = targetUser.Email,
            Channel = "App",
            Subject = $"FamilyVaultInvite:{member.VaultMemberId}",
            Message = $"{owner.FullName} invited you to join {vault.VaultName}.",
            SendStatus = "Pending",
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToMemberResponse(member));
    }

    [HttpPost("invitations/{memberId:guid}/respond")]
    public async Task<ActionResult> RespondToInvitation(Guid memberId, [FromBody] RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var email = NormalizeEmail(user.Email);
        var member = await _dbContext.FamilyVaultMembers
            .FirstOrDefaultAsync(item =>
                item.VaultMemberId == memberId &&
                item.InviteStatus == "Pending" &&
                (item.UserId == user.UserId || (item.InviteEmail != null && item.InviteEmail.ToLower() == email)),
                cancellationToken);

        if (member is null)
        {
            return NotFound(new { message = "Invitation was not found." });
        }

        var now = DateTime.UtcNow;
        member.UserId = user.UserId;
        member.MemberName = user.FullName;
        member.InviteEmail = user.Email;
        member.MemberRole = "Contributor";

        if (request.Accept)
        {
            await _dbContext.FamilyVaultMembers
                .Where(item =>
                    item.UserId == user.UserId &&
                    item.VaultMemberId != memberId &&
                    item.InviteStatus == "Accepted")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.InviteStatus, "Removed")
                    .SetProperty(item => item.AcceptedAt, (DateTime?)null),
                    cancellationToken);
        }

        member.InviteStatus = request.Accept ? "Accepted" : "Rejected";
        member.AcceptedAt = request.Accept ? now : null;

        await _dbContext.NotificationLogs
            .Where(item => item.UserId == user.UserId && item.Subject == $"FamilyVaultInvite:{memberId}")
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.SendStatus, "Sent"), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { message = request.Accept ? "Invitation accepted." : "Invitation rejected." });
    }

    [HttpPost("posts")]
    [RequestSizeLimit(100_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)]
    public async Task<ActionResult<FamilyVaultPostResponse>> CreatePost([FromForm] CreateFamilyPostRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var vault = await GetCurrentDisplayVaultAsync(user, cancellationToken);
        if (vault is null)
        {
            vault = await GetOrCreateOwnedVaultAsync(user, cancellationToken);
        }

        var title = request.Title?.Trim();
        var description = request.Description?.Trim();
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description) && request.File is null)
        {
            return BadRequest(new { message = "Add a short story, a title, or an image." });
        }

        if (request.File is not null && !IsAllowedPostFile(request.File))
        {
            return BadRequest(new { message = "Post files must be images or videos up to 80 MB." });
        }

        var now = DateTime.UtcNow;
        var memory = new Memory
        {
            MemoryId = Guid.NewGuid(),
            VaultId = vault.VaultId,
            CreatedByUserId = user.UserId,
            Title = string.IsNullOrWhiteSpace(title) ? "Family memory" : title,
            Description = description,
            MemoryDate = DateOnly.FromDateTime(now),
            Visibility = "Family",
            CreatedAt = now
        };

        if (request.File is not null)
        {
            CloudUploadResult uploaded;
            try
            {
                uploaded = await _fileStorage.UploadAsync(request.File, user.UserId, cancellationToken);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"Unable to upload file. {ex.Message}" });
            }

            var storedFile = new StoredFile
            {
                FileId = Guid.NewGuid(),
                OwnerUserId = user.UserId,
                OriginalFileName = uploaded.OriginalFileName,
                StoredFileName = uploaded.StoredFileName,
                FileUrl = uploaded.FileUrl,
                MimeType = uploaded.MimeType,
                FileSizeBytes = uploaded.FileSizeBytes,
                Sha256Hash = uploaded.Sha256Hash,
                StoragePurpose = "family-vault-post",
                EncryptionStatus = "Plain",
                CreatedAt = now
            };

            memory.MemoryFiles.Add(new MemoryFile
            {
                MemoryFileId = Guid.NewGuid(),
                MemoryId = memory.MemoryId,
                FileId = storedFile.FileId,
                File = storedFile,
                Caption = title,
                CreatedAt = now
            });
        }

        _dbContext.Memories.Add(memory);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var created = await LoadPostAsync(memory.MemoryId, user.UserId, cancellationToken);
        return Ok(created);
    }

    [HttpPost("posts/{memoryId:guid}/like")]
    public async Task<ActionResult<FamilyPostReactionResponse>> ToggleLike(Guid memoryId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await CanAccessMemoryAsync(memoryId, userId.Value, cancellationToken))
        {
            return NotFound(new { message = "Post was not found." });
        }

        var existing = await _dbContext.MemoryLikes
            .FirstOrDefaultAsync(item => item.MemoryId == memoryId && item.UserId == userId.Value, cancellationToken);
        var liked = existing is null;
        if (existing is null)
        {
            _dbContext.MemoryLikes.Add(new MemoryLike
            {
                MemoryLikeId = Guid.NewGuid(),
                MemoryId = memoryId,
                UserId = userId.Value,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            _dbContext.MemoryLikes.Remove(existing);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var likeCount = await _dbContext.MemoryLikes.CountAsync(item => item.MemoryId == memoryId, cancellationToken);
        var commentCount = await _dbContext.MemoryComments.CountAsync(item => item.MemoryId == memoryId && !item.IsDeleted, cancellationToken);
        return Ok(new FamilyPostReactionResponse(liked, likeCount, commentCount));
    }

    [HttpPost("posts/{memoryId:guid}/comments")]
    public async Task<ActionResult<FamilyPostCommentResponse>> AddComment(Guid memoryId, [FromBody] AddFamilyCommentRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var text = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(new { message = "Write a comment first." });
        }

        if (!await CanAccessMemoryAsync(memoryId, userId.Value, cancellationToken))
        {
            return NotFound(new { message = "Post was not found." });
        }

        if (request.ParentCommentId is not null)
        {
            var parentExists = await _dbContext.MemoryComments.AnyAsync(item =>
                item.CommentId == request.ParentCommentId.Value &&
                item.MemoryId == memoryId &&
                !item.IsDeleted,
                cancellationToken);
            if (!parentExists)
            {
                return BadRequest(new { message = "The comment you are replying to was not found." });
            }
        }

        var comment = new MemoryComment
        {
            CommentId = Guid.NewGuid(),
            MemoryId = memoryId,
            UserId = userId.Value,
            ParentCommentId = request.ParentCommentId,
            CommentText = text,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        _dbContext.MemoryComments.Add(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var user = await _dbContext.Users.AsNoTracking().FirstAsync(item => item.UserId == userId.Value, cancellationToken);
        return Ok(new FamilyPostCommentResponse(
            comment.CommentId,
            comment.ParentCommentId,
            user.FullName,
            user.AvatarUrl,
            comment.CommentText,
            comment.CreatedAt));
    }

    [HttpPut("posts/{memoryId:guid}")]
    [RequestSizeLimit(100_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)]
    public async Task<ActionResult<FamilyVaultPostResponse>> UpdatePost(Guid memoryId, [FromForm] UpdateFamilyPostRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _dbContext.Memories
            .Include(item => item.MemoryFiles)
                .ThenInclude(mf => mf.File)
            .FirstOrDefaultAsync(item => item.MemoryId == memoryId && item.CreatedByUserId == userId.Value, cancellationToken);

        if (post is null)
        {
            return NotFound(new { message = "Post was not found or you do not have permission to edit it." });
        }

        var title = request.Title?.Trim();
        var description = request.Description?.Trim();

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description) && post.MemoryFiles.Count == 0 && request.File is null)
        {
            return BadRequest(new { message = "Add a short story, a title, or an image." });
        }

        var now = DateTime.UtcNow;
        post.Title = string.IsNullOrWhiteSpace(title) ? (post.Title) : title;
        post.Description = description;

        if (request.File is not null)
        {
            if (!IsAllowedPostFile(request.File))
            {
                return BadRequest(new { message = "Post files must be images or videos up to 80 MB." });
            }

            CloudUploadResult uploaded;
            try
            {
                uploaded = await _fileStorage.UploadAsync(request.File, userId.Value, cancellationToken);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"Unable to upload file. {ex.Message}" });
            }

            var storedFile = new StoredFile
            {
                FileId = Guid.NewGuid(),
                OwnerUserId = userId.Value,
                OriginalFileName = uploaded.OriginalFileName,
                StoredFileName = uploaded.StoredFileName,
                FileUrl = uploaded.FileUrl,
                MimeType = uploaded.MimeType,
                FileSizeBytes = uploaded.FileSizeBytes,
                Sha256Hash = uploaded.Sha256Hash,
                StoragePurpose = "family-vault-post",
                EncryptionStatus = "Plain",
                CreatedAt = now
            };

            // Use direct SQL delete to fully bypass EF Core Change Tracker and avoid
            // DbUpdateConcurrencyException caused by ClientSetNull generating extra UPDATE before DELETE.
            var oldMemoryFileIds = post.MemoryFiles.Select(mf => mf.MemoryFileId).ToList();
            post.MemoryFiles.Clear();
            _dbContext.ChangeTracker.Clear();

            if (oldMemoryFileIds.Count > 0)
            {
                await _dbContext.MemoryFiles
                    .Where(mf => oldMemoryFileIds.Contains(mf.MemoryFileId))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            _dbContext.StoredFiles.Add(storedFile);
            _dbContext.MemoryFiles.Add(new MemoryFile
            {
                MemoryFileId = Guid.NewGuid(),
                MemoryId = memoryId,
                FileId = storedFile.FileId,
                Caption = title,
                CreatedAt = now
            });

            // Re-attach post for the title/description update
            var postToUpdate = await _dbContext.Memories
                .FirstOrDefaultAsync(item => item.MemoryId == memoryId && item.CreatedByUserId == userId.Value, cancellationToken);
            if (postToUpdate is not null)
            {
                postToUpdate.Title = string.IsNullOrWhiteSpace(title) ? postToUpdate.Title : title;
                postToUpdate.Description = description;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var refreshed = await LoadPostAsync(post.MemoryId, userId.Value, cancellationToken);
        return refreshed is null
            ? NotFound(new { message = "Post was not found after saving." })
            : Ok(refreshed);
    }

    [HttpDelete("posts/{memoryId:guid}")]
    public async Task<ActionResult> DeletePost(Guid memoryId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _dbContext.Memories
            .Include(item => item.MemoryFiles)
            .Include(item => item.MemoryComments)
            .FirstOrDefaultAsync(item => item.MemoryId == memoryId && item.CreatedByUserId == userId.Value, cancellationToken);

        if (post is null)
        {
            return NotFound(new { message = "Post was not found or you do not have permission to delete it." });
        }

        // Delete likes via separate query since Memory model doesn't have MemoryLikes navigation property
        var likes = await _dbContext.MemoryLikes.Where(item => item.MemoryId == memoryId).ToListAsync(cancellationToken);
        _dbContext.MemoryLikes.RemoveRange(likes);
        _dbContext.MemoryComments.RemoveRange(post.MemoryComments);
        _dbContext.MemoryFiles.RemoveRange(post.MemoryFiles);
        _dbContext.Memories.Remove(post);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Post deleted successfully." });
    }

    private async Task<FamilyVault?> GetCurrentDisplayVaultAsync(User user, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(user.Email);
        var joinedVaultId = await _dbContext.FamilyVaultMembers
            .Where(item =>
                item.InviteStatus == "Accepted" &&
                item.Vault.OwnerUserId != user.UserId &&
                (item.UserId == user.UserId || (item.InviteEmail != null && item.InviteEmail.ToLower() == email)))
            .OrderBy(item => item.InvitedAt)
            .Select(item => (Guid?)item.VaultId)
            .FirstOrDefaultAsync(cancellationToken);

        if (joinedVaultId is not null)
        {
            return await _dbContext.FamilyVaults.FirstOrDefaultAsync(item => item.VaultId == joinedVaultId.Value && item.IsActive, cancellationToken);
        }

        return await _dbContext.FamilyVaults
            .FirstOrDefaultAsync(item => item.OwnerUserId == user.UserId && item.IsActive, cancellationToken);
    }

    private async Task<FamilyVault> GetOrCreateOwnedVaultAsync(User user, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.FamilyVaults
            .FirstOrDefaultAsync(item => item.OwnerUserId == user.UserId && item.IsActive, cancellationToken);
        if (existing is not null)
        {
            var ownerMembershipExists = await _dbContext.FamilyVaultMembers.AnyAsync(item =>
                item.VaultId == existing.VaultId &&
                item.UserId == user.UserId &&
                item.InviteStatus == "Accepted",
                cancellationToken);
            if (!ownerMembershipExists)
            {
                _dbContext.FamilyVaultMembers.Add(new FamilyVaultMember
                {
                    VaultMemberId = Guid.NewGuid(),
                    VaultId = existing.VaultId,
                    UserId = user.UserId,
                    InviteEmail = user.Email,
                    MemberName = user.FullName,
                    MemberRole = "Owner",
                    InviteStatus = "Accepted",
                    InvitedAt = DateTime.UtcNow,
                    AcceptedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return existing;
        }

        var now = DateTime.UtcNow;
        var vault = new FamilyVault
        {
            VaultId = Guid.NewGuid(),
            OwnerUserId = user.UserId,
            VaultName = $"{user.FullName}'s Family Vault",
            Description = "A private shared album for family memories.",
            CreatedAt = now,
            IsActive = true
        };

        vault.FamilyVaultMembers.Add(new FamilyVaultMember
        {
            VaultMemberId = Guid.NewGuid(),
            VaultId = vault.VaultId,
            UserId = user.UserId,
            InviteEmail = user.Email,
            MemberName = user.FullName,
            MemberRole = "Owner",
            InviteStatus = "Accepted",
            InvitedAt = now,
            AcceptedAt = now
        });

        _dbContext.FamilyVaults.Add(vault);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return vault;
    }

    private async Task<FamilyVaultResponse> ToVaultResponseAsync(Guid vaultId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var vault = await _dbContext.FamilyVaults
            .AsNoTracking()
            .Include(item => item.FamilyVaultMembers)
                .ThenInclude(member => member.User)
            .FirstAsync(item => item.VaultId == vaultId, cancellationToken);

        var posts = await _dbContext.Memories
            .AsNoTracking()
            .Include(item => item.CreatedByUser)
            .Include(item => item.MemoryFiles)
                .ThenInclude(memoryFile => memoryFile.File)
            .Include(item => item.MemoryComments.Where(comment => !comment.IsDeleted))
                .ThenInclude(comment => comment.User)
            .Where(item => item.VaultId == vaultId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var memoryIds = posts.Select(item => item.MemoryId).ToList();
        var likeCounts = await _dbContext.MemoryLikes
            .AsNoTracking()
            .Where(item => memoryIds.Contains(item.MemoryId))
            .GroupBy(item => item.MemoryId)
            .Select(group => new { MemoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.MemoryId, item => item.Count, cancellationToken);
        var likedIds = await _dbContext.MemoryLikes
            .AsNoTracking()
            .Where(item => item.UserId == currentUserId && memoryIds.Contains(item.MemoryId))
            .Select(item => item.MemoryId)
            .ToListAsync(cancellationToken);

        return new FamilyVaultResponse(
            vault.VaultId,
            vault.VaultName,
            vault.Description,
            vault.FamilyVaultMembers
                .OrderBy(item => item.MemberRole == "Owner" ? 0 : 1)
                .ThenBy(item => item.MemberName)
                .Select(ToMemberResponse)
                .ToList(),
            posts.Select(post => ToPostResponse(post, currentUserId, likeCounts.GetValueOrDefault(post.MemoryId), likedIds.Contains(post.MemoryId))).ToList());
    }

    private async Task<FamilyVaultPostResponse?> LoadPostAsync(Guid memoryId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var post = await _dbContext.Memories
            .AsNoTracking()
            .Include(item => item.CreatedByUser)
            .Include(item => item.MemoryFiles)
                .ThenInclude(memoryFile => memoryFile.File)
            .Include(item => item.MemoryComments.Where(comment => !comment.IsDeleted))
                .ThenInclude(comment => comment.User)
            .FirstOrDefaultAsync(item => item.MemoryId == memoryId, cancellationToken);
        if (post is null)
        {
            return null;
        }

        var likeCount = await _dbContext.MemoryLikes.CountAsync(item => item.MemoryId == memoryId, cancellationToken);
        var likedByMe = await _dbContext.MemoryLikes.AnyAsync(item => item.MemoryId == memoryId && item.UserId == currentUserId, cancellationToken);
        return ToPostResponse(post, currentUserId, likeCount, likedByMe);
    }

    private static FamilyVaultMemberResponse ToMemberResponse(FamilyVaultMember member)
    {
        return new FamilyVaultMemberResponse(
            member.VaultMemberId,
            member.UserId,
            member.MemberName ?? member.User?.FullName ?? member.InviteEmail ?? "Member",
            member.InviteEmail ?? member.User?.Email,
            member.MemberRole,
            member.InviteStatus,
            member.AcceptedAt,
            member.User?.AvatarUrl);
    }

    private static FamilyVaultPostResponse ToPostResponse(Memory post, Guid currentUserId, int likeCount, bool likedByMe)
    {
        var primaryFile = post.MemoryFiles.OrderBy(item => item.CreatedAt).FirstOrDefault()?.File;
        var comments = post.MemoryComments
            .Where(item => !item.IsDeleted)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new FamilyPostCommentResponse(
                item.CommentId,
                item.ParentCommentId,
                item.User.FullName,
                item.User.AvatarUrl,
                item.CommentText,
                item.CreatedAt))
            .ToList();

        return new FamilyVaultPostResponse(
            post.MemoryId,
            post.Title,
            post.Description,
            post.CreatedAt,
            post.MemoryDate,
            post.CreatedByUserId,
            post.CreatedByUser.FullName,
            post.CreatedByUser.AvatarUrl,
            primaryFile?.FileUrl,
            primaryFile?.MimeType,
            primaryFile?.OriginalFileName,
            likeCount,
            comments.Count,
            likedByMe,
            comments);
    }

    private async Task<bool> CanAccessMemoryAsync(Guid memoryId, Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Memories.AnyAsync(memory =>
            memory.MemoryId == memoryId &&
            _dbContext.FamilyVaultMembers.Any(member =>
                member.VaultId == memory.VaultId &&
                member.InviteStatus == "Accepted" &&
                member.UserId == userId),
            cancellationToken);
    }

    private static bool IsAllowedPostFile(IFormFile file)
    {
        if (file.Length <= 0 || file.Length > MaxPostFileBytes)
        {
            return false;
        }

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        return AllowedPostMimePrefixes.Any(prefix => contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
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
}

public sealed class InviteFamilyMemberRequest
{
    public string? Email { get; set; }
}

public sealed class RespondInvitationRequest
{
    public bool Accept { get; set; }
}

public sealed class CreateFamilyPostRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public IFormFile? File { get; set; }
}

public sealed class UpdateFamilyPostRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public IFormFile? File { get; set; }
}

public sealed class AddFamilyCommentRequest
{
    public string? Text { get; set; }
    public Guid? ParentCommentId { get; set; }
}

public sealed record FamilyVaultResponse(
    Guid VaultId,
    string VaultName,
    string? Description,
    IReadOnlyList<FamilyVaultMemberResponse> Members,
    IReadOnlyList<FamilyVaultPostResponse> Posts);

public sealed record FamilyVaultMemberResponse(
    Guid VaultMemberId,
    Guid? UserId,
    string Name,
    string? Email,
    string Role,
    string Status,
    DateTime? AcceptedAt,
    string? AvatarUrl);

public sealed record FamilyVaultInvitationResponse(
    Guid VaultMemberId,
    Guid VaultId,
    string VaultName,
    string OwnerName,
    DateTime InvitedAt);

public sealed record FamilyVaultPostResponse(
    Guid MemoryId,
    string Title,
    string? Description,
    DateTime CreatedAt,
    DateOnly? MemoryDate,
    Guid AuthorUserId,
    string AuthorName,
    string? AuthorAvatarUrl,
    string? FileUrl,
    string? FileMimeType,
    string? FileName,
    int LikeCount,
    int CommentCount,
    bool LikedByMe,
    IReadOnlyList<FamilyPostCommentResponse> Comments);

public sealed record FamilyPostCommentResponse(
    Guid CommentId,
    Guid? ParentCommentId,
    string AuthorName,
    string? AuthorAvatarUrl,
    string Text,
    DateTime CreatedAt);

public sealed record FamilyPostReactionResponse(
    bool LikedByMe,
    int LikeCount,
    int CommentCount);

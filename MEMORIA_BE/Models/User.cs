using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class User
{
    public Guid UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public string PasswordHash { get; set; } = null!;

    public string? AvatarUrl { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? Address { get; set; }

    public string? CccdNumber { get; set; }

    public DateOnly? CccdIssuedDate { get; set; }

    public string? CccdIssuedPlace { get; set; }

    public bool IsEmailVerified { get; set; }

    public bool IsPhoneVerified { get; set; }

    public bool IsActive { get; set; }

    public string UserStatus { get; set; } = null!;

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<AuthVerificationCode> AuthVerificationCodes { get; set; } = new List<AuthVerificationCode>();

    public virtual ICollection<Beneficiary> Beneficiaries { get; set; } = new List<Beneficiary>();

    public virtual ICollection<DecryptionApproval> DecryptionApprovals { get; set; } = new List<DecryptionApproval>();

    public virtual ICollection<DigitalAsset> DigitalAssets { get; set; } = new List<DigitalAsset>();

    public virtual ICollection<EkycVerification> EkycVerifications { get; set; } = new List<EkycVerification>();

    public virtual ICollection<EmergencyReleaseEvent> EmergencyReleaseEvents { get; set; } = new List<EmergencyReleaseEvent>();

    public virtual ICollection<EmergencyReleaseRecipient> EmergencyReleaseRecipients { get; set; } = new List<EmergencyReleaseRecipient>();

    public virtual ICollection<FamilyVaultMember> FamilyVaultMembers { get; set; } = new List<FamilyVaultMember>();

    public virtual ICollection<FamilyVault> FamilyVaults { get; set; } = new List<FamilyVault>();

    public virtual ICollection<FutureLetter> FutureLetters { get; set; } = new List<FutureLetter>();

    public virtual ICollection<Guardian> Guardians { get; set; } = new List<Guardian>();

    public virtual ICollection<LegacyPlan> LegacyPlans { get; set; } = new List<LegacyPlan>();

    public virtual ICollection<Memory> Memories { get; set; } = new List<Memory>();

    public virtual ICollection<MemoryComment> MemoryComments { get; set; } = new List<MemoryComment>();

    public virtual ICollection<NotificationLog> NotificationLogs { get; set; } = new List<NotificationLog>();

    public virtual ICollection<StoredFile> StoredFiles { get; set; } = new List<StoredFile>();

    public virtual TimeCoinWallet? TimeCoinWallet { get; set; }

    public virtual ICollection<UnlockRequestReview> UnlockRequestReviews { get; set; } = new List<UnlockRequestReview>();

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public virtual UserSecuritySetting? UserSecuritySetting { get; set; }

    public virtual ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();
}

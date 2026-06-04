using System;
using System.Collections.Generic;
using MEMORIA_BE.Models;
using Microsoft.EntityFrameworkCore;

namespace MEMORIA_BE.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<AuthVerificationCode> AuthVerificationCodes { get; set; }

    public virtual DbSet<Beneficiary> Beneficiaries { get; set; }

    public virtual DbSet<DecryptionApproval> DecryptionApprovals { get; set; }

    public virtual DbSet<DigitalAsset> DigitalAssets { get; set; }

    public virtual DbSet<DigitalAssetCategory> DigitalAssetCategories { get; set; }

    public virtual DbSet<EkycVerification> EkycVerifications { get; set; }

    public virtual DbSet<EmergencyReleaseEvent> EmergencyReleaseEvents { get; set; }

    public virtual DbSet<EmergencyReleaseRecipient> EmergencyReleaseRecipients { get; set; }

    public virtual DbSet<FamilyVault> FamilyVaults { get; set; }

    public virtual DbSet<FamilyVaultMember> FamilyVaultMembers { get; set; }

    public virtual DbSet<FutureLetter> FutureLetters { get; set; }

    public virtual DbSet<FutureLetterAttachment> FutureLetterAttachments { get; set; }

    public virtual DbSet<FutureLetterRecipient> FutureLetterRecipients { get; set; }

    public virtual DbSet<Guardian> Guardians { get; set; }

    public virtual DbSet<LegacyPlan> LegacyPlans { get; set; }

    public virtual DbSet<LegacyPlanAsset> LegacyPlanAssets { get; set; }

    public virtual DbSet<LegacyPlanBeneficiary> LegacyPlanBeneficiaries { get; set; }

    public virtual DbSet<LegacyUnlockRequest> LegacyUnlockRequests { get; set; }

    public virtual DbSet<LegalDocumentSubmission> LegalDocumentSubmissions { get; set; }

    public virtual DbSet<Memory> Memories { get; set; }

    public virtual DbSet<MemoryComment> MemoryComments { get; set; }

    public virtual DbSet<MemoryFile> MemoryFiles { get; set; }

    public virtual DbSet<MemoryLike> MemoryLikes { get; set; }

    public virtual DbSet<NotificationLog> NotificationLogs { get; set; }

    public virtual DbSet<ProofOfLifeCheckin> ProofOfLifeCheckins { get; set; }

    public virtual DbSet<ProofOfLifeSchedule> ProofOfLifeSchedules { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<ScheduledDeliveryLog> ScheduledDeliveryLogs { get; set; }

    public virtual DbSet<StoredFile> StoredFiles { get; set; }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<TimeCoinTransaction> TimeCoinTransactions { get; set; }

    public virtual DbSet<TimeCoinWallet> TimeCoinWallets { get; set; }

    public virtual DbSet<UnlockRequestReview> UnlockRequestReviews { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<UserSecuritySetting> UserSecuritySettings { get; set; }

    public virtual DbSet<UserSubscription> UserSubscriptions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditLogId).HasName("PK__AuditLog__EB5F6CBDDAD56101");

            entity.Property(e => e.AuditLogId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.ActionName).HasMaxLength(150);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.EntityId).HasMaxLength(100);
            entity.Property(e => e.EntityName).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_AuditLogs_Users");
        });

        modelBuilder.Entity<AuthVerificationCode>(entity =>
        {
            entity.HasKey(e => e.VerificationId).HasName("PK__AuthVeri__306D4907A542B52A");

            entity.HasIndex(e => new { e.UserId, e.Purpose, e.ExpiresAt }, "IX_AuthVerificationCodes_User_Purpose");

            entity.Property(e => e.VerificationId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CodeHash).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Purpose).HasMaxLength(40);

            entity.HasOne(d => d.User).WithMany(p => p.AuthVerificationCodes)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AuthVerificationCodes_Users");
        });

        modelBuilder.Entity<Beneficiary>(entity =>
        {
            entity.HasKey(e => e.BeneficiaryId).HasName("PK__Benefici__3FBA95F586BE25F2");

            entity.Property(e => e.BeneficiaryId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.IdentityDocumentHash).HasMaxLength(128);
            entity.Property(e => e.IdentityDocumentMasked).HasMaxLength(50);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.Relationship).HasMaxLength(100);

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.Beneficiaries)
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Beneficiaries_Users");
        });

        modelBuilder.Entity<DecryptionApproval>(entity =>
        {
            entity.HasKey(e => e.DecryptionApprovalId).HasName("PK__Decrypti__216AB005014412D7");

            entity.Property(e => e.DecryptionApprovalId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.ApprovalStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Approved");
            entity.Property(e => e.ApprovedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Note).HasMaxLength(1000);

            entity.HasOne(d => d.ApprovedByUser).WithMany(p => p.DecryptionApprovals)
                .HasForeignKey(d => d.ApprovedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DecryptionApprovals_Users");

            entity.HasOne(d => d.UnlockRequest).WithMany(p => p.DecryptionApprovals)
                .HasForeignKey(d => d.UnlockRequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DecryptionApprovals_Requests");
        });

        modelBuilder.Entity<DigitalAsset>(entity =>
        {
            entity.HasKey(e => e.AssetId).HasName("PK__DigitalA__43492352A3B97C0C");

            entity.HasIndex(e => new { e.OwnerUserId, e.CategoryId }, "IX_DigitalAssets_Owner_Category");

            entity.Property(e => e.AssetId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.AssetDescription).HasMaxLength(1000);
            entity.Property(e => e.AssetName).HasMaxLength(200);
            entity.Property(e => e.AssetStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Active");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Category).WithMany(p => p.DigitalAssets)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DigitalAssets_Categories");

            entity.HasOne(d => d.File).WithMany(p => p.DigitalAssets)
                .HasForeignKey(d => d.FileId)
                .HasConstraintName("FK_DigitalAssets_Files");

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.DigitalAssets)
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DigitalAssets_Users");
        });

        modelBuilder.Entity<DigitalAssetCategory>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK__DigitalA__19093A0BEEE6719B");

            entity.HasIndex(e => e.CategoryName, "UQ__DigitalA__8517B2E0E6236C1A").IsUnique();

            entity.Property(e => e.CategoryName).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(255);
        });

        modelBuilder.Entity<EkycVerification>(entity =>
        {
            entity.HasKey(e => e.EkycId).HasName("PK__EkycVeri__BF627DDDA0FF7693");

            entity.Property(e => e.EkycId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.BackImageUrl).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.DocumentNumberMasked).HasMaxLength(50);
            entity.Property(e => e.DocumentType).HasMaxLength(50);
            entity.Property(e => e.FrontImageUrl).HasMaxLength(1000);
            entity.Property(e => e.RejectReason).HasMaxLength(1000);
            entity.Property(e => e.SelfieImageUrl).HasMaxLength(1000);
            entity.Property(e => e.VerificationStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");

            entity.HasOne(d => d.User).WithMany(p => p.EkycVerifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Ekyc_Users");
        });

        modelBuilder.Entity<EmergencyReleaseEvent>(entity =>
        {
            entity.HasKey(e => e.EmergencyReleaseId).HasName("PK__Emergenc__7242D625FC9976B1");

            entity.Property(e => e.EmergencyReleaseId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.EventStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Preparing");
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.StartedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.TriggerReason).HasMaxLength(500);

            entity.HasOne(d => d.TriggeredByUser).WithMany(p => p.EmergencyReleaseEvents)
                .HasForeignKey(d => d.TriggeredByUserId)
                .HasConstraintName("FK_EmergencyRelease_Users");
        });

        modelBuilder.Entity<EmergencyReleaseRecipient>(entity =>
        {
            entity.HasKey(e => e.EmergencyReleaseRecipientId).HasName("PK__Emergenc__A1D7517B8EDFDD68");

            entity.Property(e => e.EmergencyReleaseRecipientId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.DeliveryStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.RecipientEmail).HasMaxLength(255);
            entity.Property(e => e.RecipientName).HasMaxLength(150);
            entity.Property(e => e.RecipientPhone).HasMaxLength(20);
            entity.Property(e => e.RecipientZalo).HasMaxLength(100);

            entity.HasOne(d => d.EmergencyRelease).WithMany(p => p.EmergencyReleaseRecipients)
                .HasForeignKey(d => d.EmergencyReleaseId)
                .HasConstraintName("FK_ERR_Events");

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.EmergencyReleaseRecipients)
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ERR_Owner");

            entity.HasOne(d => d.ReleasePackageFile).WithMany(p => p.EmergencyReleaseRecipients)
                .HasForeignKey(d => d.ReleasePackageFileId)
                .HasConstraintName("FK_ERR_File");
        });

        modelBuilder.Entity<FamilyVault>(entity =>
        {
            entity.HasKey(e => e.VaultId).HasName("PK__FamilyVa__3FF7D1BB02346CF4");

            entity.Property(e => e.VaultId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.VaultName).HasMaxLength(150);

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.FamilyVaults)
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FamilyVaults_Users");
        });

        modelBuilder.Entity<FamilyVaultMember>(entity =>
        {
            entity.HasKey(e => e.VaultMemberId).HasName("PK__FamilyVa__0BFC91187241C19F");

            entity.Property(e => e.VaultMemberId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.InviteEmail).HasMaxLength(255);
            entity.Property(e => e.InviteStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");
            entity.Property(e => e.InvitedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.MemberName).HasMaxLength(150);
            entity.Property(e => e.MemberRole)
                .HasMaxLength(30)
                .HasDefaultValue("Viewer");

            entity.HasOne(d => d.User).WithMany(p => p.FamilyVaultMembers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_VaultMembers_Users");

            entity.HasOne(d => d.Vault).WithMany(p => p.FamilyVaultMembers)
                .HasForeignKey(d => d.VaultId)
                .HasConstraintName("FK_VaultMembers_Vaults");
        });

        modelBuilder.Entity<FutureLetter>(entity =>
        {
            entity.HasKey(e => e.LetterId).HasName("PK__FutureLe__AE46E8F1D7D61A4C");

            entity.HasIndex(e => new { e.OwnerUserId, e.SealStatus, e.DeliveryDate }, "IX_FutureLetters_Owner_Status_Delivery");

            entity.Property(e => e.LetterId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.SealStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Draft");
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.FutureLetters)
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FutureLetters_Users");
        });

        modelBuilder.Entity<FutureLetterAttachment>(entity =>
        {
            entity.HasKey(e => e.LetterAttachmentId).HasName("PK__FutureLe__FFDC804E5F267981");

            entity.Property(e => e.LetterAttachmentId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.File).WithMany(p => p.FutureLetterAttachments)
                .HasForeignKey(d => d.FileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LetterAttachments_Files");

            entity.HasOne(d => d.Letter).WithMany(p => p.FutureLetterAttachments)
                .HasForeignKey(d => d.LetterId)
                .HasConstraintName("FK_LetterAttachments_Letters");
        });

        modelBuilder.Entity<FutureLetterRecipient>(entity =>
        {
            entity.HasKey(e => e.RecipientId).HasName("PK__FutureLe__F0A6024D021277A1");

            entity.Property(e => e.RecipientId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.DeliveryChannel)
                .HasMaxLength(30)
                .HasDefaultValue("Email");
            entity.Property(e => e.RecipientEmail).HasMaxLength(255);
            entity.Property(e => e.RecipientName).HasMaxLength(150);
            entity.Property(e => e.RecipientPhone).HasMaxLength(20);
            entity.Property(e => e.RecipientZalo).HasMaxLength(100);
            entity.Property(e => e.Relationship).HasMaxLength(100);

            entity.HasOne(d => d.Letter).WithMany(p => p.FutureLetterRecipients)
                .HasForeignKey(d => d.LetterId)
                .HasConstraintName("FK_LetterRecipients_Letters");
        });

        modelBuilder.Entity<Guardian>(entity =>
        {
            entity.HasKey(e => e.GuardianId).HasName("PK__Guardian__0A5E1A9B1A2377E1");

            entity.Property(e => e.GuardianId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.Relationship).HasMaxLength(100);
            entity.Property(e => e.VerificationStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.Guardians)
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Guardians_Users");
        });

        modelBuilder.Entity<LegacyPlan>(entity =>
        {
            entity.HasKey(e => e.LegacyPlanId).HasName("PK__LegacyPl__AAA8994571255996");

            entity.HasIndex(e => new { e.OwnerUserId, e.PlanStatus }, "IX_LegacyPlans_Owner_Status");

            entity.Property(e => e.LegacyPlanId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.IsEcontractSigned).HasColumnName("IsEContractSigned");
            entity.Property(e => e.PlanName).HasMaxLength(200);
            entity.Property(e => e.PlanStatus)
                .HasMaxLength(40)
                .HasDefaultValue("Draft");

            entity.HasOne(d => d.ContractFile).WithMany(p => p.LegacyPlans)
                .HasForeignKey(d => d.ContractFileId)
                .HasConstraintName("FK_LegacyPlans_ContractFile");

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.LegacyPlans)
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LegacyPlans_Users");
        });

        modelBuilder.Entity<LegacyPlanAsset>(entity =>
        {
            entity.HasKey(e => e.LegacyPlanAssetId).HasName("PK__LegacyPl__1AC839E5308205B7");

            entity.Property(e => e.LegacyPlanAssetId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.ReleaseInstruction).HasMaxLength(1000);

            entity.HasOne(d => d.Asset).WithMany(p => p.LegacyPlanAssets)
                .HasForeignKey(d => d.AssetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LPA_Assets");

            entity.HasOne(d => d.Beneficiary).WithMany(p => p.LegacyPlanAssets)
                .HasForeignKey(d => d.BeneficiaryId)
                .HasConstraintName("FK_LPA_Beneficiaries");

            entity.HasOne(d => d.LegacyPlan).WithMany(p => p.LegacyPlanAssets)
                .HasForeignKey(d => d.LegacyPlanId)
                .HasConstraintName("FK_LPA_Plans");
        });

        modelBuilder.Entity<LegacyPlanBeneficiary>(entity =>
        {
            entity.HasKey(e => e.LegacyPlanBeneficiaryId).HasName("PK__LegacyPl__93A1462713A22C67");

            entity.Property(e => e.LegacyPlanBeneficiaryId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.AccessLevel)
                .HasMaxLength(30)
                .HasDefaultValue("Partial");
            entity.Property(e => e.Note).HasMaxLength(1000);

            entity.HasOne(d => d.Beneficiary).WithMany(p => p.LegacyPlanBeneficiaries)
                .HasForeignKey(d => d.BeneficiaryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LPB_Beneficiaries");

            entity.HasOne(d => d.LegacyPlan).WithMany(p => p.LegacyPlanBeneficiaries)
                .HasForeignKey(d => d.LegacyPlanId)
                .HasConstraintName("FK_LPB_Plans");
        });

        modelBuilder.Entity<LegacyUnlockRequest>(entity =>
        {
            entity.HasKey(e => e.UnlockRequestId).HasName("PK__LegacyUn__549DA8EB8B584166");

            entity.HasIndex(e => e.RequestStatus, "IX_UnlockRequests_Status");

            entity.Property(e => e.UnlockRequestId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.RequestReason).HasMaxLength(1000);
            entity.Property(e => e.ClaimTokenHash).HasMaxLength(128);
            entity.Property(e => e.RequestStatus)
                .HasMaxLength(40)
                .HasDefaultValue("Submitted");
            entity.Property(e => e.SubmittedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.LegacyPlan).WithMany(p => p.LegacyUnlockRequests)
                .HasForeignKey(d => d.LegacyPlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UnlockRequests_Plans");

            entity.HasOne(d => d.RequestedByBeneficiary).WithMany(p => p.LegacyUnlockRequests)
                .HasForeignKey(d => d.RequestedByBeneficiaryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UnlockRequests_Beneficiaries");
        });

        modelBuilder.Entity<LegalDocumentSubmission>(entity =>
        {
            entity.HasKey(e => e.LegalDocumentId).HasName("PK__LegalDoc__B09E0FA54CF67609");

            entity.Property(e => e.LegalDocumentId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.DocumentType).HasMaxLength(50);
            entity.Property(e => e.HumanReviewStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");
            entity.Property(e => e.OcrConfidence).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.OcrStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");
            entity.Property(e => e.RejectReason).HasMaxLength(1000);

            entity.HasOne(d => d.File).WithMany(p => p.LegalDocumentSubmissions)
                .HasForeignKey(d => d.FileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LegalDocs_Files");

            entity.HasOne(d => d.UnlockRequest).WithMany(p => p.LegalDocumentSubmissions)
                .HasForeignKey(d => d.UnlockRequestId)
                .HasConstraintName("FK_LegalDocs_Requests");
        });

        modelBuilder.Entity<Memory>(entity =>
        {
            entity.HasKey(e => e.MemoryId).HasName("PK__Memories__9A4986D4C25D3973");

            entity.HasIndex(e => new { e.VaultId, e.MemoryDate }, "IX_Memories_Vault_Date");

            entity.Property(e => e.MemoryId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Latitude).HasColumnType("decimal(10, 7)");
            entity.Property(e => e.LocationName).HasMaxLength(255);
            entity.Property(e => e.Longitude).HasColumnType("decimal(10, 7)");
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Visibility)
                .HasMaxLength(30)
                .HasDefaultValue("Family");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.Memories)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Memories_Users");

            entity.HasOne(d => d.Vault).WithMany(p => p.Memories)
                .HasForeignKey(d => d.VaultId)
                .HasConstraintName("FK_Memories_Vaults");
        });

        modelBuilder.Entity<MemoryComment>(entity =>
        {
            entity.HasKey(e => e.CommentId).HasName("PK__MemoryCo__C3B4DFCAC0589B7C");

            entity.Property(e => e.CommentId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CommentText).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Memory).WithMany(p => p.MemoryComments)
                .HasForeignKey(d => d.MemoryId)
                .HasConstraintName("FK_MemoryComments_Memories");

            entity.HasOne(d => d.User).WithMany(p => p.MemoryComments)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MemoryComments_Users");
        });

        modelBuilder.Entity<MemoryFile>(entity =>
        {
            entity.HasKey(e => e.MemoryFileId).HasName("PK__MemoryFi__A6B90354713EF024");

            entity.Property(e => e.MemoryFileId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Caption).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.File).WithMany(p => p.MemoryFiles)
                .HasForeignKey(d => d.FileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MemoryFiles_Files");

            entity.HasOne(d => d.Memory).WithMany(p => p.MemoryFiles)
                .HasForeignKey(d => d.MemoryId)
                .HasConstraintName("FK_MemoryFiles_Memories");
        });

        modelBuilder.Entity<MemoryLike>(entity =>
        {
            entity.HasKey(e => e.MemoryLikeId).HasName("PK_MemoryLikes");

            entity.HasIndex(e => new { e.MemoryId, e.UserId }, "IX_MemoryLikes_Memory_User").IsUnique();

            entity.Property(e => e.MemoryLikeId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Memory).WithMany()
                .HasForeignKey(d => d.MemoryId)
                .HasConstraintName("FK_MemoryLikes_Memories");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MemoryLikes_Users");
        });

        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("PK__Notifica__20CF2E123DF37A8F");

            entity.HasIndex(e => new { e.SendStatus, e.CreatedAt }, "IX_NotificationLogs_Status");

            entity.Property(e => e.NotificationId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Channel).HasMaxLength(30);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.RecipientEmail).HasMaxLength(255);
            entity.Property(e => e.RecipientPhone).HasMaxLength(20);
            entity.Property(e => e.SendStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");
            entity.Property(e => e.Subject).HasMaxLength(255);

            entity.HasOne(d => d.User).WithMany(p => p.NotificationLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Notifications_Users");
        });

        modelBuilder.Entity<ProofOfLifeCheckin>(entity =>
        {
            entity.HasKey(e => e.CheckinId).HasName("PK__ProofOfL__F3C85D71D027D673");

            entity.HasIndex(e => new { e.CheckinStatus, e.ResponseDeadline }, "IX_ProofOfLifeCheckins_Status");

            entity.Property(e => e.CheckinId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Channel).HasMaxLength(30);
            entity.Property(e => e.CheckinStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Sent");
            entity.Property(e => e.FailureReason).HasMaxLength(1000);
            entity.Property(e => e.SentAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Schedule).WithMany(p => p.ProofOfLifeCheckins)
                .HasForeignKey(d => d.ScheduleId)
                .HasConstraintName("FK_POLCheckins_Schedules");
        });

        modelBuilder.Entity<ProofOfLifeSchedule>(entity =>
        {
            entity.HasKey(e => e.ScheduleId).HasName("PK__ProofOfL__9C8A5B49438125CE");

            entity.Property(e => e.ScheduleId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CheckIntervalDays).HasDefaultValue(30);
            entity.Property(e => e.CheckIntervalMinutes).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.GracePeriodDays).HasDefaultValue(14);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsConfigurationLocked).HasDefaultValue(false);
            entity.Property(e => e.MaxFailedAttempts).HasDefaultValue(3);
            entity.Property(e => e.PreferredChannel)
                .HasMaxLength(30)
                .HasDefaultValue("Email");

            entity.HasOne(d => d.LegacyPlan).WithMany(p => p.ProofOfLifeSchedules)
                .HasForeignKey(d => d.LegacyPlanId)
                .HasConstraintName("FK_POLSchedules_Plans");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1A100C7262");

            entity.HasIndex(e => e.RoleName, "UQ__Roles__8A2B6160E7362134").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.RoleName).HasMaxLength(50);
        });

        modelBuilder.Entity<ScheduledDeliveryLog>(entity =>
        {
            entity.HasKey(e => e.DeliveryLogId).HasName("PK__Schedule__D6A7BAEE65564233");

            entity.Property(e => e.DeliveryLogId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.DeliveryStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

            entity.HasOne(d => d.Letter).WithMany(p => p.ScheduledDeliveryLogs)
                .HasForeignKey(d => d.LetterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DeliveryLogs_Letters");

            entity.HasOne(d => d.Recipient).WithMany(p => p.ScheduledDeliveryLogs)
                .HasForeignKey(d => d.RecipientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DeliveryLogs_Recipients");
        });

        modelBuilder.Entity<StoredFile>(entity =>
        {
            entity.HasKey(e => e.FileId).HasName("PK__StoredFi__6F0F98BF692F8FD0");

            entity.HasIndex(e => e.OwnerUserId, "IX_StoredFiles_Owner");

            entity.Property(e => e.FileId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.EncryptionStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Encrypted");
            entity.Property(e => e.FileUrl).HasMaxLength(1000);
            entity.Property(e => e.MimeType).HasMaxLength(100);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.Sha256Hash).HasMaxLength(128);
            entity.Property(e => e.StoragePurpose).HasMaxLength(50);
            entity.Property(e => e.StoredFileName).HasMaxLength(255);

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.StoredFiles)
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StoredFiles_Users");
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId).HasName("PK__Subscrip__755C22B77620E3B0");

            entity.HasIndex(e => e.PlanCode, "UQ__Subscrip__DDC8069B290F7FEA").IsUnique();

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PlanCode).HasMaxLength(30);
            entity.Property(e => e.PlanName).HasMaxLength(100);
            entity.Property(e => e.PriceVnd).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<TimeCoinTransaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId).HasName("PK__TimeCoin__55433A6BC6B65C34");

            entity.Property(e => e.TransactionId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.BalanceAfter).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ExternalPaymentCode).HasMaxLength(100);
            entity.Property(e => e.PaymentMethod).HasMaxLength(50);
            entity.Property(e => e.TransactionType).HasMaxLength(30);

            entity.HasOne(d => d.Wallet).WithMany(p => p.TimeCoinTransactions)
                .HasForeignKey(d => d.WalletId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Transactions_Wallets");
        });

        modelBuilder.Entity<TimeCoinWallet>(entity =>
        {
            entity.HasKey(e => e.WalletId).HasName("PK__TimeCoin__84D4F90EB6775481");

            entity.HasIndex(e => e.UserId, "UQ__TimeCoin__1788CC4D7BD93816").IsUnique();

            entity.Property(e => e.WalletId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Balance).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.User).WithOne(p => p.TimeCoinWallet)
                .HasForeignKey<TimeCoinWallet>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Wallets_Users");
        });

        modelBuilder.Entity<UnlockRequestReview>(entity =>
        {
            entity.HasKey(e => e.ReviewId).HasName("PK__UnlockRe__74BC79CE7E84D2CF");

            entity.Property(e => e.ReviewId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.ReviewDecision).HasMaxLength(30);
            entity.Property(e => e.ReviewNote).HasMaxLength(1000);
            entity.Property(e => e.ReviewedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.ReviewerUser).WithMany(p => p.UnlockRequestReviews)
                .HasForeignKey(d => d.ReviewerUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Reviews_Reviewers");

            entity.HasOne(d => d.UnlockRequest).WithMany(p => p.UnlockRequestReviews)
                .HasForeignKey(d => d.UnlockRequestId)
                .HasConstraintName("FK_Reviews_Requests");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C3F8C72BA");

            entity.HasIndex(e => e.Email, "IX_Users_Email");

            entity.HasIndex(e => e.Email, "UQ__Users__A9D105344494D30B").IsUnique();

            entity.HasIndex(e => e.PhoneNumber, "UX_Users_PhoneNumber_NotNull")
                .IsUnique()
                .HasFilter("([PhoneNumber] IS NOT NULL)");

            entity.Property(e => e.UserId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.AvatarUrl).HasMaxLength(1000);
            entity.Property(e => e.CccdIssuedPlace).HasMaxLength(255);
            entity.Property(e => e.CccdNumber).HasMaxLength(30);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.Gender).HasMaxLength(20);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.UserStatus)
                .HasMaxLength(40)
                .HasDefaultValue("Active");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId }).HasName("PK__UserRole__AF2760AD73F08B29");

            entity.Property(e => e.AssignedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserRoles_Roles");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserRoles_Users");
        });

        modelBuilder.Entity<UserSecuritySetting>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__UserSecu__1788CC4CF2DDCC4D");

            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.PreferredOtpChannel).HasMaxLength(20);
            entity.Property(e => e.RecoveryEmail).HasMaxLength(255);
            entity.Property(e => e.RecoveryPhone).HasMaxLength(20);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.User).WithOne(p => p.UserSecuritySetting)
                .HasForeignKey<UserSecuritySetting>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Security_Users");
        });

        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(e => e.SubscriptionId).HasName("PK__UserSubs__9A2B249D231FC255");

            entity.Property(e => e.SubscriptionId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.StartDate).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.SubscriptionStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Active");

            entity.HasOne(d => d.Plan).WithMany(p => p.UserSubscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Subscriptions_Plans");

            entity.HasOne(d => d.User).WithMany(p => p.UserSubscriptions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Subscriptions_Users");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

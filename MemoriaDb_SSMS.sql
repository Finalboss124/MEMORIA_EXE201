
-- =========================================================
-- MEMORIA - HOP KY UC / DIGITAL MEMORY & LEGACY PLATFORM
-- SQL Server / SSMS Database Schema v1
-- =========================================================

IF DB_ID(N'MemoriaDb') IS NOT NULL
BEGIN
    ALTER DATABASE MemoriaDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE MemoriaDb;
END
GO

CREATE DATABASE MemoriaDb;
GO

USE MemoriaDb;
GO

-- =========================================================
-- 1. AUTHENTICATION / USER / EKYC
-- =========================================================

CREATE TABLE Roles (
    RoleId INT IDENTITY(1,1) PRIMARY KEY,
    RoleName NVARCHAR(50) NOT NULL UNIQUE, -- Admin, User, Reviewer, Support
    Description NVARCHAR(255) NULL
);

CREATE TABLE Users (
    UserId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(255) NOT NULL UNIQUE,
    PhoneNumber NVARCHAR(20) NULL,
    PasswordHash NVARCHAR(500) NOT NULL,
    AvatarUrl NVARCHAR(1000) NULL,
    DateOfBirth DATE NULL,
    Gender NVARCHAR(20) NULL,
    Address NVARCHAR(500) NULL,
    IsEmailVerified BIT NOT NULL DEFAULT 0,
    IsPhoneVerified BIT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    LastLoginAt DATETIME2 NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt DATETIME2 NULL
);

CREATE TABLE UserRoles (
    UserId UNIQUEIDENTIFIER NOT NULL,
    RoleId INT NOT NULL,
    AssignedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES Roles(RoleId)
);

CREATE TABLE EkycVerifications (
    EkycId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UserId UNIQUEIDENTIFIER NOT NULL,
    DocumentType NVARCHAR(50) NOT NULL, -- CCCD, Passport
    DocumentNumberMasked NVARCHAR(50) NOT NULL,
    FrontImageUrl NVARCHAR(1000) NOT NULL,
    BackImageUrl NVARCHAR(1000) NULL,
    SelfieImageUrl NVARCHAR(1000) NULL,
    VerificationStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Approved, Rejected
    RejectReason NVARCHAR(1000) NULL,
    VerifiedAt DATETIME2 NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Ekyc_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT CK_Ekyc_Status CHECK (VerificationStatus IN ('Pending','Approved','Rejected'))
);

CREATE TABLE UserSecuritySettings (
    UserId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    IsTwoFactorEnabled BIT NOT NULL DEFAULT 0,
    PreferredOtpChannel NVARCHAR(20) NULL, -- Email, SMS, Zalo
    RecoveryEmail NVARCHAR(255) NULL,
    RecoveryPhone NVARCHAR(20) NULL,
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Security_Users FOREIGN KEY (UserId) REFERENCES Users(UserId)
);

CREATE TABLE AuthVerificationCodes (
    VerificationId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UserId UNIQUEIDENTIFIER NOT NULL,
    CodeHash NVARCHAR(500) NOT NULL,
    Purpose NVARCHAR(40) NOT NULL, -- Login, Register, GoogleLogin
    ExpiresAt DATETIME2 NOT NULL,
    ConsumedAt DATETIME2 NULL,
    AttemptCount INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_AuthVerificationCodes_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT CK_AuthVerificationCodes_Purpose CHECK (Purpose IN ('Login','Register','GoogleLogin'))
);

-- =========================================================
-- 2. SUBSCRIPTION / TIMECOIN PAYMENT
-- =========================================================

CREATE TABLE SubscriptionPlans (
    PlanId INT IDENTITY(1,1) PRIMARY KEY,
    PlanCode NVARCHAR(30) NOT NULL UNIQUE, -- Freemium, Starter, Standard, Premium
    PlanName NVARCHAR(100) NOT NULL,
    PriceVnd DECIMAL(18,2) NOT NULL DEFAULT 0,
    StorageLimitGb INT NOT NULL,
    FutureLetterLimit INT NULL, -- NULL = unlimited
    HasFamilyVault BIT NOT NULL DEFAULT 0,
    HasLegacyTransfer BIT NOT NULL DEFAULT 0,
    HasProofOfLife BIT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1
);

CREATE TABLE UserSubscriptions (
    SubscriptionId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UserId UNIQUEIDENTIFIER NOT NULL,
    PlanId INT NOT NULL,
    StartDate DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    EndDate DATETIME2 NULL,
    SubscriptionStatus NVARCHAR(30) NOT NULL DEFAULT 'Active', -- Active, Expired, Cancelled
    AutoRenew BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Subscriptions_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT FK_Subscriptions_Plans FOREIGN KEY (PlanId) REFERENCES SubscriptionPlans(PlanId),
    CONSTRAINT CK_Subscription_Status CHECK (SubscriptionStatus IN ('Active','Expired','Cancelled'))
);

CREATE TABLE TimeCoinWallets (
    WalletId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UserId UNIQUEIDENTIFIER NOT NULL UNIQUE,
    Balance DECIMAL(18,2) NOT NULL DEFAULT 0,
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Wallets_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT CK_Wallet_Balance CHECK (Balance >= 0)
);

CREATE TABLE TimeCoinTransactions (
    TransactionId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    WalletId UNIQUEIDENTIFIER NOT NULL,
    TransactionType NVARCHAR(30) NOT NULL, -- TopUp, PurchasePlan, Refund, Adjustment
    Amount DECIMAL(18,2) NOT NULL,
    BalanceAfter DECIMAL(18,2) NOT NULL,
    PaymentMethod NVARCHAR(50) NULL, -- Momo, VNPAY, BankTransfer
    ExternalPaymentCode NVARCHAR(100) NULL,
    Description NVARCHAR(500) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Transactions_Wallets FOREIGN KEY (WalletId) REFERENCES TimeCoinWallets(WalletId),
    CONSTRAINT CK_Transaction_Type CHECK (TransactionType IN ('TopUp','PurchasePlan','Refund','Adjustment'))
);

-- =========================================================
-- 3. FILE STORAGE - COMMON STORAGE METADATA
-- =========================================================

CREATE TABLE StoredFiles (
    FileId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    OwnerUserId UNIQUEIDENTIFIER NOT NULL,
    OriginalFileName NVARCHAR(255) NOT NULL,
    StoredFileName NVARCHAR(255) NOT NULL,
    FileUrl NVARCHAR(1000) NOT NULL,
    MimeType NVARCHAR(100) NOT NULL,
    FileSizeBytes BIGINT NOT NULL,
    Sha256Hash NVARCHAR(128) NULL,
    EncryptionStatus NVARCHAR(30) NOT NULL DEFAULT 'Encrypted', -- Plain, Encrypted
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_StoredFiles_Users FOREIGN KEY (OwnerUserId) REFERENCES Users(UserId),
    CONSTRAINT CK_File_Size CHECK (FileSizeBytes >= 0),
    CONSTRAINT CK_File_Encryption CHECK (EncryptionStatus IN ('Plain','Encrypted'))
);

-- =========================================================
-- 4. FUTURE POSTBOX / LETTER SCHEDULING
-- =========================================================

CREATE TABLE FutureLetters (
    LetterId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    OwnerUserId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    BodyEncrypted NVARCHAR(MAX) NULL,
    DeliveryDate DATETIME2 NOT NULL,
    SealStatus NVARCHAR(30) NOT NULL DEFAULT 'Draft', -- Draft, Sealed, Scheduled, Delivered, Cancelled
    IsLocked BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    SealedAt DATETIME2 NULL,
    DeliveredAt DATETIME2 NULL,
    CancelledAt DATETIME2 NULL,
    CONSTRAINT FK_FutureLetters_Users FOREIGN KEY (OwnerUserId) REFERENCES Users(UserId),
    CONSTRAINT CK_FutureLetters_Status CHECK (SealStatus IN ('Draft','Sealed','Scheduled','Delivered','Cancelled'))
);

CREATE TABLE FutureLetterRecipients (
    RecipientId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LetterId UNIQUEIDENTIFIER NOT NULL,
    RecipientName NVARCHAR(150) NOT NULL,
    RecipientEmail NVARCHAR(255) NULL,
    RecipientPhone NVARCHAR(20) NULL,
    RecipientZalo NVARCHAR(100) NULL,
    Relationship NVARCHAR(100) NULL, -- Self, Child, Spouse, Parent, Friend
    DeliveryChannel NVARCHAR(30) NOT NULL DEFAULT 'Email', -- Email, SMS, Zalo
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_LetterRecipients_Letters FOREIGN KEY (LetterId) REFERENCES FutureLetters(LetterId) ON DELETE CASCADE,
    CONSTRAINT CK_Recipient_Channel CHECK (DeliveryChannel IN ('Email','SMS','Zalo'))
);

CREATE TABLE FutureLetterAttachments (
    LetterAttachmentId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LetterId UNIQUEIDENTIFIER NOT NULL,
    FileId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_LetterAttachments_Letters FOREIGN KEY (LetterId) REFERENCES FutureLetters(LetterId) ON DELETE CASCADE,
    CONSTRAINT FK_LetterAttachments_Files FOREIGN KEY (FileId) REFERENCES StoredFiles(FileId)
);

CREATE TABLE ScheduledDeliveryLogs (
    DeliveryLogId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LetterId UNIQUEIDENTIFIER NOT NULL,
    RecipientId UNIQUEIDENTIFIER NOT NULL,
    DeliveryStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Sent, Failed
    AttemptCount INT NOT NULL DEFAULT 0,
    LastAttemptAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(1000) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_DeliveryLogs_Letters FOREIGN KEY (LetterId) REFERENCES FutureLetters(LetterId),
    CONSTRAINT FK_DeliveryLogs_Recipients FOREIGN KEY (RecipientId) REFERENCES FutureLetterRecipients(RecipientId),
    CONSTRAINT CK_Delivery_Status CHECK (DeliveryStatus IN ('Pending','Sent','Failed'))
);

-- =========================================================
-- 5. FAMILY VAULT / MEMORIES
-- =========================================================

CREATE TABLE FamilyVaults (
    VaultId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    OwnerUserId UNIQUEIDENTIFIER NOT NULL,
    VaultName NVARCHAR(150) NOT NULL,
    Description NVARCHAR(1000) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_FamilyVaults_Users FOREIGN KEY (OwnerUserId) REFERENCES Users(UserId)
);

CREATE TABLE FamilyVaultMembers (
    VaultMemberId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    VaultId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NULL,
    InviteEmail NVARCHAR(255) NULL,
    MemberName NVARCHAR(150) NULL,
    MemberRole NVARCHAR(30) NOT NULL DEFAULT 'Viewer', -- Owner, Contributor, Viewer
    InviteStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Accepted, Rejected, Removed
    InvitedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    AcceptedAt DATETIME2 NULL,
    CONSTRAINT FK_VaultMembers_Vaults FOREIGN KEY (VaultId) REFERENCES FamilyVaults(VaultId) ON DELETE CASCADE,
    CONSTRAINT FK_VaultMembers_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT CK_VaultMember_Role CHECK (MemberRole IN ('Owner','Contributor','Viewer')),
    CONSTRAINT CK_VaultMember_Status CHECK (InviteStatus IN ('Pending','Accepted','Rejected','Removed'))
);

CREATE TABLE Memories (
    MemoryId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    VaultId UNIQUEIDENTIFIER NOT NULL,
    CreatedByUserId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    MemoryDate DATE NULL,
    LocationName NVARCHAR(255) NULL,
    Latitude DECIMAL(10,7) NULL,
    Longitude DECIMAL(10,7) NULL,
    Visibility NVARCHAR(30) NOT NULL DEFAULT 'Family', -- Private, Family
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt DATETIME2 NULL,
    CONSTRAINT FK_Memories_Vaults FOREIGN KEY (VaultId) REFERENCES FamilyVaults(VaultId) ON DELETE CASCADE,
    CONSTRAINT FK_Memories_Users FOREIGN KEY (CreatedByUserId) REFERENCES Users(UserId),
    CONSTRAINT CK_Memory_Visibility CHECK (Visibility IN ('Private','Family'))
);

CREATE TABLE MemoryFiles (
    MemoryFileId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    MemoryId UNIQUEIDENTIFIER NOT NULL,
    FileId UNIQUEIDENTIFIER NOT NULL,
    Caption NVARCHAR(500) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_MemoryFiles_Memories FOREIGN KEY (MemoryId) REFERENCES Memories(MemoryId) ON DELETE CASCADE,
    CONSTRAINT FK_MemoryFiles_Files FOREIGN KEY (FileId) REFERENCES StoredFiles(FileId)
);

CREATE TABLE MemoryComments (
    CommentId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    MemoryId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    CommentText NVARCHAR(1000) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    CONSTRAINT FK_MemoryComments_Memories FOREIGN KEY (MemoryId) REFERENCES Memories(MemoryId) ON DELETE CASCADE,
    CONSTRAINT FK_MemoryComments_Users FOREIGN KEY (UserId) REFERENCES Users(UserId)
);

-- =========================================================
-- 6. LEGACY TRANSFER / BENEFICIARY / DIGITAL ASSET
-- =========================================================

CREATE TABLE Beneficiaries (
    BeneficiaryId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    OwnerUserId UNIQUEIDENTIFIER NOT NULL,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(255) NULL,
    PhoneNumber NVARCHAR(20) NULL,
    Relationship NVARCHAR(100) NOT NULL,
    IdentityDocumentMasked NVARCHAR(50) NULL,
    IsPrimary BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Beneficiaries_Users FOREIGN KEY (OwnerUserId) REFERENCES Users(UserId)
);

CREATE TABLE Guardians (
    GuardianId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    OwnerUserId UNIQUEIDENTIFIER NOT NULL,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(255) NULL,
    PhoneNumber NVARCHAR(20) NULL,
    Relationship NVARCHAR(100) NOT NULL,
    VerificationStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Verified, Rejected
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Guardians_Users FOREIGN KEY (OwnerUserId) REFERENCES Users(UserId),
    CONSTRAINT CK_Guardian_Status CHECK (VerificationStatus IN ('Pending','Verified','Rejected'))
);

CREATE TABLE DigitalAssetCategories (
    CategoryId INT IDENTITY(1,1) PRIMARY KEY,
    CategoryName NVARCHAR(100) NOT NULL UNIQUE, -- Password, EWallet, CryptoWallet, BankInfo, ImportantDocument, Instruction
    Description NVARCHAR(255) NULL
);

CREATE TABLE DigitalAssets (
    AssetId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    OwnerUserId UNIQUEIDENTIFIER NOT NULL,
    CategoryId INT NOT NULL,
    AssetName NVARCHAR(200) NOT NULL,
    AssetDescription NVARCHAR(1000) NULL,
    EncryptedSecret NVARCHAR(MAX) NULL, -- encrypted password / recovery phrase / instruction
    FileId UNIQUEIDENTIFIER NULL,
    AssetStatus NVARCHAR(30) NOT NULL DEFAULT 'Active', -- Active, Archived, Deleted
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt DATETIME2 NULL,
    CONSTRAINT FK_DigitalAssets_Users FOREIGN KEY (OwnerUserId) REFERENCES Users(UserId),
    CONSTRAINT FK_DigitalAssets_Categories FOREIGN KEY (CategoryId) REFERENCES DigitalAssetCategories(CategoryId),
    CONSTRAINT FK_DigitalAssets_Files FOREIGN KEY (FileId) REFERENCES StoredFiles(FileId),
    CONSTRAINT CK_DigitalAsset_Status CHECK (AssetStatus IN ('Active','Archived','Deleted'))
);

CREATE TABLE LegacyPlans (
    LegacyPlanId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    OwnerUserId UNIQUEIDENTIFIER NOT NULL,
    PlanName NVARCHAR(200) NOT NULL,
    ContractFileId UNIQUEIDENTIFIER NULL,
    IsEContractSigned BIT NOT NULL DEFAULT 0,
    ContractSignedAt DATETIME2 NULL,
    PlanStatus NVARCHAR(40) NOT NULL DEFAULT 'Draft', -- Draft, Active, FrozenPendingLegalVerification, ApprovedForRelease, Released, Cancelled
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt DATETIME2 NULL,
    CONSTRAINT FK_LegacyPlans_Users FOREIGN KEY (OwnerUserId) REFERENCES Users(UserId),
    CONSTRAINT FK_LegacyPlans_ContractFile FOREIGN KEY (ContractFileId) REFERENCES StoredFiles(FileId),
    CONSTRAINT CK_LegacyPlan_Status CHECK (PlanStatus IN ('Draft','Active','FrozenPendingLegalVerification','ApprovedForRelease','Released','Cancelled'))
);

CREATE TABLE LegacyPlanBeneficiaries (
    LegacyPlanBeneficiaryId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LegacyPlanId UNIQUEIDENTIFIER NOT NULL,
    BeneficiaryId UNIQUEIDENTIFIER NOT NULL,
    AccessLevel NVARCHAR(30) NOT NULL DEFAULT 'Partial', -- Partial, Full
    Note NVARCHAR(1000) NULL,
    CONSTRAINT FK_LPB_Plans FOREIGN KEY (LegacyPlanId) REFERENCES LegacyPlans(LegacyPlanId) ON DELETE CASCADE,
    CONSTRAINT FK_LPB_Beneficiaries FOREIGN KEY (BeneficiaryId) REFERENCES Beneficiaries(BeneficiaryId),
    CONSTRAINT CK_LPB_Access CHECK (AccessLevel IN ('Partial','Full'))
);

CREATE TABLE LegacyPlanAssets (
    LegacyPlanAssetId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LegacyPlanId UNIQUEIDENTIFIER NOT NULL,
    AssetId UNIQUEIDENTIFIER NOT NULL,
    BeneficiaryId UNIQUEIDENTIFIER NULL,
    ReleaseInstruction NVARCHAR(1000) NULL,
    CONSTRAINT FK_LPA_Plans FOREIGN KEY (LegacyPlanId) REFERENCES LegacyPlans(LegacyPlanId) ON DELETE CASCADE,
    CONSTRAINT FK_LPA_Assets FOREIGN KEY (AssetId) REFERENCES DigitalAssets(AssetId),
    CONSTRAINT FK_LPA_Beneficiaries FOREIGN KEY (BeneficiaryId) REFERENCES Beneficiaries(BeneficiaryId)
);

-- =========================================================
-- 7. PROOF OF LIFE
-- =========================================================

CREATE TABLE ProofOfLifeSchedules (
    ScheduleId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LegacyPlanId UNIQUEIDENTIFIER NOT NULL,
    CheckIntervalDays INT NOT NULL DEFAULT 30,
    GracePeriodDays INT NOT NULL DEFAULT 14,
    MaxFailedAttempts INT NOT NULL DEFAULT 3,
    PreferredChannel NVARCHAR(30) NOT NULL DEFAULT 'Email', -- App, Email, SMS, Zalo
    IsActive BIT NOT NULL DEFAULT 1,
    NextCheckAt DATETIME2 NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_POLSchedules_Plans FOREIGN KEY (LegacyPlanId) REFERENCES LegacyPlans(LegacyPlanId) ON DELETE CASCADE,
    CONSTRAINT CK_POL_Channel CHECK (PreferredChannel IN ('App','Email','SMS','Zalo')),
    CONSTRAINT CK_POL_Interval CHECK (CheckIntervalDays > 0 AND GracePeriodDays >= 0 AND MaxFailedAttempts > 0)
);

CREATE TABLE ProofOfLifeCheckins (
    CheckinId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    ScheduleId UNIQUEIDENTIFIER NOT NULL,
    SentAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    ResponseDeadline DATETIME2 NOT NULL,
    RespondedAt DATETIME2 NULL,
    CheckinStatus NVARCHAR(30) NOT NULL DEFAULT 'Sent', -- Sent, ConfirmedAlive, Expired, Failed
    Channel NVARCHAR(30) NOT NULL,
    FailureReason NVARCHAR(1000) NULL,
    CONSTRAINT FK_POLCheckins_Schedules FOREIGN KEY (ScheduleId) REFERENCES ProofOfLifeSchedules(ScheduleId) ON DELETE CASCADE,
    CONSTRAINT CK_Checkin_Status CHECK (CheckinStatus IN ('Sent','ConfirmedAlive','Expired','Failed'))
);

-- =========================================================
-- 8. LEGAL UNLOCK / OCR / HUMAN REVIEW / DECRYPT APPROVAL
-- =========================================================

CREATE TABLE LegacyUnlockRequests (
    UnlockRequestId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LegacyPlanId UNIQUEIDENTIFIER NOT NULL,
    RequestedByBeneficiaryId UNIQUEIDENTIFIER NOT NULL,
    RequestStatus NVARCHAR(40) NOT NULL DEFAULT 'Submitted', -- Submitted, OcrChecking, HumanReview, Approved, Rejected, Released
    RequestReason NVARCHAR(1000) NULL,
    SubmittedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    DecidedAt DATETIME2 NULL,
    CONSTRAINT FK_UnlockRequests_Plans FOREIGN KEY (LegacyPlanId) REFERENCES LegacyPlans(LegacyPlanId),
    CONSTRAINT FK_UnlockRequests_Beneficiaries FOREIGN KEY (RequestedByBeneficiaryId) REFERENCES Beneficiaries(BeneficiaryId),
    CONSTRAINT CK_Unlock_Status CHECK (RequestStatus IN ('Submitted','OcrChecking','HumanReview','Approved','Rejected','Released'))
);

CREATE TABLE LegalDocumentSubmissions (
    LegalDocumentId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UnlockRequestId UNIQUEIDENTIFIER NOT NULL,
    DocumentType NVARCHAR(50) NOT NULL, -- DeathCertificate, MissingPersonCourtDecision
    FileId UNIQUEIDENTIFIER NOT NULL,
    OcrStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Passed, Failed
    OcrConfidence DECIMAL(5,2) NULL,
    OcrExtractedText NVARCHAR(MAX) NULL,
    HumanReviewStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Approved, Rejected
    RejectReason NVARCHAR(1000) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_LegalDocs_Requests FOREIGN KEY (UnlockRequestId) REFERENCES LegacyUnlockRequests(UnlockRequestId) ON DELETE CASCADE,
    CONSTRAINT FK_LegalDocs_Files FOREIGN KEY (FileId) REFERENCES StoredFiles(FileId),
    CONSTRAINT CK_LegalDoc_Type CHECK (DocumentType IN ('DeathCertificate','MissingPersonCourtDecision')),
    CONSTRAINT CK_LegalDoc_Ocr CHECK (OcrStatus IN ('Pending','Passed','Failed')),
    CONSTRAINT CK_LegalDoc_Human CHECK (HumanReviewStatus IN ('Pending','Approved','Rejected'))
);

CREATE TABLE UnlockRequestReviews (
    ReviewId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UnlockRequestId UNIQUEIDENTIFIER NOT NULL,
    ReviewerUserId UNIQUEIDENTIFIER NOT NULL,
    ReviewDecision NVARCHAR(30) NOT NULL, -- Approved, Rejected, NeedMoreInfo
    ReviewNote NVARCHAR(1000) NULL,
    ReviewedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Reviews_Requests FOREIGN KEY (UnlockRequestId) REFERENCES LegacyUnlockRequests(UnlockRequestId) ON DELETE CASCADE,
    CONSTRAINT FK_Reviews_Reviewers FOREIGN KEY (ReviewerUserId) REFERENCES Users(UserId),
    CONSTRAINT CK_Review_Decision CHECK (ReviewDecision IN ('Approved','Rejected','NeedMoreInfo'))
);

CREATE TABLE DecryptionApprovals (
    DecryptionApprovalId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UnlockRequestId UNIQUEIDENTIFIER NOT NULL,
    ApprovedByUserId UNIQUEIDENTIFIER NOT NULL,
    ApprovalStatus NVARCHAR(30) NOT NULL DEFAULT 'Approved', -- Approved, Revoked
    ApprovedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    Note NVARCHAR(1000) NULL,
    CONSTRAINT FK_DecryptionApprovals_Requests FOREIGN KEY (UnlockRequestId) REFERENCES LegacyUnlockRequests(UnlockRequestId),
    CONSTRAINT FK_DecryptionApprovals_Users FOREIGN KEY (ApprovedByUserId) REFERENCES Users(UserId),
    CONSTRAINT CK_Decryption_Status CHECK (ApprovalStatus IN ('Approved','Revoked'))
);

-- =========================================================
-- 9. EMERGENCY DATA RELEASE
-- =========================================================

CREATE TABLE EmergencyReleaseEvents (
    EmergencyReleaseId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    TriggeredByUserId UNIQUEIDENTIFIER NULL,
    TriggerReason NVARCHAR(500) NOT NULL, -- CompanyShutdown, AdminEmergency, SystemDisaster
    EventStatus NVARCHAR(30) NOT NULL DEFAULT 'Preparing', -- Preparing, Processing, Completed, Failed
    StartedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CompletedAt DATETIME2 NULL,
    Note NVARCHAR(1000) NULL,
    CONSTRAINT FK_EmergencyRelease_Users FOREIGN KEY (TriggeredByUserId) REFERENCES Users(UserId),
    CONSTRAINT CK_Emergency_Status CHECK (EventStatus IN ('Preparing','Processing','Completed','Failed'))
);

CREATE TABLE EmergencyReleaseRecipients (
    EmergencyReleaseRecipientId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    EmergencyReleaseId UNIQUEIDENTIFIER NOT NULL,
    OwnerUserId UNIQUEIDENTIFIER NOT NULL,
    RecipientName NVARCHAR(150) NOT NULL,
    RecipientEmail NVARCHAR(255) NULL,
    RecipientPhone NVARCHAR(20) NULL,
    RecipientZalo NVARCHAR(100) NULL,
    ReleasePackageFileId UNIQUEIDENTIFIER NULL,
    DeliveryStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Sent, Failed
    SentAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(1000) NULL,
    CONSTRAINT FK_ERR_Events FOREIGN KEY (EmergencyReleaseId) REFERENCES EmergencyReleaseEvents(EmergencyReleaseId) ON DELETE CASCADE,
    CONSTRAINT FK_ERR_Owner FOREIGN KEY (OwnerUserId) REFERENCES Users(UserId),
    CONSTRAINT FK_ERR_File FOREIGN KEY (ReleasePackageFileId) REFERENCES StoredFiles(FileId),
    CONSTRAINT CK_ERR_Delivery CHECK (DeliveryStatus IN ('Pending','Sent','Failed'))
);

-- =========================================================
-- 10. NOTIFICATION / AUDIT
-- =========================================================

CREATE TABLE NotificationLogs (
    NotificationId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UserId UNIQUEIDENTIFIER NULL,
    RecipientEmail NVARCHAR(255) NULL,
    RecipientPhone NVARCHAR(20) NULL,
    Channel NVARCHAR(30) NOT NULL, -- Email, SMS, Zalo, App
    Subject NVARCHAR(255) NULL,
    Message NVARCHAR(MAX) NOT NULL,
    SendStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Sent, Failed
    SentAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(1000) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT CK_Notification_Channel CHECK (Channel IN ('Email','SMS','Zalo','App')),
    CONSTRAINT CK_Notification_Status CHECK (SendStatus IN ('Pending','Sent','Failed'))
);

CREATE TABLE AuditLogs (
    AuditLogId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UserId UNIQUEIDENTIFIER NULL,
    ActionName NVARCHAR(150) NOT NULL,
    EntityName NVARCHAR(100) NULL,
    EntityId NVARCHAR(100) NULL,
    IpAddress NVARCHAR(50) NULL,
    UserAgent NVARCHAR(500) NULL,
    OldValue NVARCHAR(MAX) NULL,
    NewValue NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_AuditLogs_Users FOREIGN KEY (UserId) REFERENCES Users(UserId)
);

-- =========================================================
-- 11. INDEXES
-- =========================================================

CREATE INDEX IX_Users_Email ON Users(Email);
CREATE UNIQUE INDEX UX_Users_PhoneNumber_NotNull ON Users(PhoneNumber) WHERE PhoneNumber IS NOT NULL;
CREATE INDEX IX_AuthVerificationCodes_User_Purpose ON AuthVerificationCodes(UserId, Purpose, ExpiresAt);
CREATE INDEX IX_FutureLetters_Owner_Status_Delivery ON FutureLetters(OwnerUserId, SealStatus, DeliveryDate);
CREATE INDEX IX_StoredFiles_Owner ON StoredFiles(OwnerUserId);
CREATE INDEX IX_Memories_Vault_Date ON Memories(VaultId, MemoryDate);
CREATE INDEX IX_DigitalAssets_Owner_Category ON DigitalAssets(OwnerUserId, CategoryId);
CREATE INDEX IX_LegacyPlans_Owner_Status ON LegacyPlans(OwnerUserId, PlanStatus);
CREATE INDEX IX_ProofOfLifeCheckins_Status ON ProofOfLifeCheckins(CheckinStatus, ResponseDeadline);
CREATE INDEX IX_UnlockRequests_Status ON LegacyUnlockRequests(RequestStatus);
CREATE INDEX IX_NotificationLogs_Status ON NotificationLogs(SendStatus, CreatedAt);

-- =========================================================
-- 12. SEED DATA
-- =========================================================

INSERT INTO Roles (RoleName, Description) VALUES
(N'Admin', N'Quản trị hệ thống'),
(N'User', N'Người dùng cuối'),
(N'Reviewer', N'Nhân sự thẩm định hồ sơ pháp lý'),
(N'Support', N'Nhân sự hỗ trợ khách hàng');

DECLARE @DemoUserId UNIQUEIDENTIFIER = NEWID();

INSERT INTO Users
(UserId, FullName, Email, PhoneNumber, PasswordHash, AvatarUrl, IsEmailVerified, IsActive)
VALUES
(@DemoUserId, N'Memoria Demo', N'demo@memoria.vn', N'0900000000', N'1234', NULL, 1, 1);

INSERT INTO UserRoles (UserId, RoleId)
SELECT @DemoUserId, RoleId
FROM Roles
WHERE RoleName = N'User';

INSERT INTO SubscriptionPlans
(PlanCode, PlanName, PriceVnd, StorageLimitGb, FutureLetterLimit, HasFamilyVault, HasLegacyTransfer, HasProofOfLife)
VALUES
(N'Freemium', N'Freemium', 0, 1, 3, 0, 0, 0),
(N'Starter', N'Starter', 90000, 5, NULL, 0, 0, 0),
(N'Standard', N'Standard', 450000, 50, NULL, 1, 0, 0),
(N'Premium', N'Premium', 1000000, 200, NULL, 1, 1, 1);

INSERT INTO DigitalAssetCategories (CategoryName, Description) VALUES
(N'Password', N'Mật khẩu, thông tin đăng nhập cần bàn giao'),
(N'EWallet', N'Ví điện tử, tài khoản thanh toán số'),
(N'CryptoWallet', N'Ví crypto, seed phrase hoặc chỉ dẫn khôi phục'),
(N'BankInfo', N'Thông tin tài khoản ngân hàng hoặc chỉ dẫn tài chính'),
(N'ImportantDocument', N'Tài liệu quan trọng được mã hóa'),
(N'Instruction', N'Lời dặn, hướng dẫn xử lý tài sản số');

GO

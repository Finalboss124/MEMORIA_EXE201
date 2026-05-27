using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class StoredFile
{
    public Guid FileId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string OriginalFileName { get; set; } = null!;

    public string StoredFileName { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public string MimeType { get; set; } = null!;

    public long FileSizeBytes { get; set; }

    public string? Sha256Hash { get; set; }

    public string EncryptionStatus { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<DigitalAsset> DigitalAssets { get; set; } = new List<DigitalAsset>();

    public virtual ICollection<EmergencyReleaseRecipient> EmergencyReleaseRecipients { get; set; } = new List<EmergencyReleaseRecipient>();

    public virtual ICollection<FutureLetterAttachment> FutureLetterAttachments { get; set; } = new List<FutureLetterAttachment>();

    public virtual ICollection<LegacyPlan> LegacyPlans { get; set; } = new List<LegacyPlan>();

    public virtual ICollection<LegalDocumentSubmission> LegalDocumentSubmissions { get; set; } = new List<LegalDocumentSubmission>();

    public virtual ICollection<MemoryFile> MemoryFiles { get; set; } = new List<MemoryFile>();

    public virtual User OwnerUser { get; set; } = null!;
}

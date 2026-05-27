using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class LegalDocumentSubmission
{
    public Guid LegalDocumentId { get; set; }

    public Guid UnlockRequestId { get; set; }

    public string DocumentType { get; set; } = null!;

    public Guid FileId { get; set; }

    public string OcrStatus { get; set; } = null!;

    public decimal? OcrConfidence { get; set; }

    public string? OcrExtractedText { get; set; }

    public string HumanReviewStatus { get; set; } = null!;

    public string? RejectReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual StoredFile File { get; set; } = null!;

    public virtual LegacyUnlockRequest UnlockRequest { get; set; } = null!;
}

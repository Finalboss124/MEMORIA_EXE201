using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class LegacyUnlockRequest
{
    public Guid UnlockRequestId { get; set; }

    public Guid LegacyPlanId { get; set; }

    public Guid RequestedByBeneficiaryId { get; set; }

    public string RequestStatus { get; set; } = null!;

    public string? RequestReason { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public virtual ICollection<DecryptionApproval> DecryptionApprovals { get; set; } = new List<DecryptionApproval>();

    public virtual LegacyPlan LegacyPlan { get; set; } = null!;

    public virtual ICollection<LegalDocumentSubmission> LegalDocumentSubmissions { get; set; } = new List<LegalDocumentSubmission>();

    public virtual Beneficiary RequestedByBeneficiary { get; set; } = null!;

    public virtual ICollection<UnlockRequestReview> UnlockRequestReviews { get; set; } = new List<UnlockRequestReview>();
}

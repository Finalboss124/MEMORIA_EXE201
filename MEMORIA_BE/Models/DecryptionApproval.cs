using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class DecryptionApproval
{
    public Guid DecryptionApprovalId { get; set; }

    public Guid UnlockRequestId { get; set; }

    public Guid ApprovedByUserId { get; set; }

    public string ApprovalStatus { get; set; } = null!;

    public DateTime ApprovedAt { get; set; }

    public string? Note { get; set; }

    public virtual User ApprovedByUser { get; set; } = null!;

    public virtual LegacyUnlockRequest UnlockRequest { get; set; } = null!;
}

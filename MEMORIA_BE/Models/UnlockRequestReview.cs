using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class UnlockRequestReview
{
    public Guid ReviewId { get; set; }

    public Guid UnlockRequestId { get; set; }

    public Guid ReviewerUserId { get; set; }

    public string ReviewDecision { get; set; } = null!;

    public string? ReviewNote { get; set; }

    public DateTime ReviewedAt { get; set; }

    public virtual User ReviewerUser { get; set; } = null!;

    public virtual LegacyUnlockRequest UnlockRequest { get; set; } = null!;
}

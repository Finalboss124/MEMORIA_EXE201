using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class EkycVerification
{
    public Guid EkycId { get; set; }

    public Guid UserId { get; set; }

    public string DocumentType { get; set; } = null!;

    public string DocumentNumberMasked { get; set; } = null!;

    public string FrontImageUrl { get; set; } = null!;

    public string? BackImageUrl { get; set; }

    public string? SelfieImageUrl { get; set; }

    public string VerificationStatus { get; set; } = null!;

    public string? RejectReason { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}

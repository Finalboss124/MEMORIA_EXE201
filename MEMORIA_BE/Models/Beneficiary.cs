using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class Beneficiary
{
    public Guid BeneficiaryId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string Relationship { get; set; } = null!;

    public string? IdentityDocumentMasked { get; set; }

    public string? IdentityDocumentHash { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<LegacyPlanAsset> LegacyPlanAssets { get; set; } = new List<LegacyPlanAsset>();

    public virtual ICollection<LegacyPlanBeneficiary> LegacyPlanBeneficiaries { get; set; } = new List<LegacyPlanBeneficiary>();

    public virtual ICollection<LegacyUnlockRequest> LegacyUnlockRequests { get; set; } = new List<LegacyUnlockRequest>();

    public virtual User OwnerUser { get; set; } = null!;
}

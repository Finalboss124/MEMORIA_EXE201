using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class LegacyPlanAsset
{
    public Guid LegacyPlanAssetId { get; set; }

    public Guid LegacyPlanId { get; set; }

    public Guid AssetId { get; set; }

    public Guid? BeneficiaryId { get; set; }

    public string? ReleaseInstruction { get; set; }

    public virtual DigitalAsset Asset { get; set; } = null!;

    public virtual Beneficiary? Beneficiary { get; set; }

    public virtual LegacyPlan LegacyPlan { get; set; } = null!;
}

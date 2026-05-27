using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class LegacyPlanBeneficiary
{
    public Guid LegacyPlanBeneficiaryId { get; set; }

    public Guid LegacyPlanId { get; set; }

    public Guid BeneficiaryId { get; set; }

    public string AccessLevel { get; set; } = null!;

    public string? Note { get; set; }

    public virtual Beneficiary Beneficiary { get; set; } = null!;

    public virtual LegacyPlan LegacyPlan { get; set; } = null!;
}

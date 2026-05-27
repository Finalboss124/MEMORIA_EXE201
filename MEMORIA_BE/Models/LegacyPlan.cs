using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class LegacyPlan
{
    public Guid LegacyPlanId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string PlanName { get; set; } = null!;

    public Guid? ContractFileId { get; set; }

    public bool IsEcontractSigned { get; set; }

    public DateTime? ContractSignedAt { get; set; }

    public string PlanStatus { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual StoredFile? ContractFile { get; set; }

    public virtual ICollection<LegacyPlanAsset> LegacyPlanAssets { get; set; } = new List<LegacyPlanAsset>();

    public virtual ICollection<LegacyPlanBeneficiary> LegacyPlanBeneficiaries { get; set; } = new List<LegacyPlanBeneficiary>();

    public virtual ICollection<LegacyUnlockRequest> LegacyUnlockRequests { get; set; } = new List<LegacyUnlockRequest>();

    public virtual User OwnerUser { get; set; } = null!;

    public virtual ICollection<ProofOfLifeSchedule> ProofOfLifeSchedules { get; set; } = new List<ProofOfLifeSchedule>();
}

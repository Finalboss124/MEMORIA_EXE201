using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class SubscriptionPlan
{
    public int PlanId { get; set; }

    public string PlanCode { get; set; } = null!;

    public string PlanName { get; set; } = null!;

    public decimal PriceVnd { get; set; }

    public int StorageLimitGb { get; set; }

    public int? FutureLetterLimit { get; set; }

    public bool HasFamilyVault { get; set; }

    public bool HasLegacyTransfer { get; set; }

    public bool HasProofOfLife { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();
}

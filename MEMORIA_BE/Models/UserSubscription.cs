using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class UserSubscription
{
    public Guid SubscriptionId { get; set; }

    public Guid UserId { get; set; }

    public int PlanId { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string SubscriptionStatus { get; set; } = null!;

    public bool AutoRenew { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual SubscriptionPlan Plan { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

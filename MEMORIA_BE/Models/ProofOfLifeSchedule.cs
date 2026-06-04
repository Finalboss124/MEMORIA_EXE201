using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class ProofOfLifeSchedule
{
    public Guid ScheduleId { get; set; }

    public Guid LegacyPlanId { get; set; }

    public int CheckIntervalDays { get; set; }

    public int CheckIntervalMinutes { get; set; }

    public int GracePeriodDays { get; set; }

    public int MaxFailedAttempts { get; set; }

    public string PreferredChannel { get; set; } = null!;

    public bool IsActive { get; set; }

    public bool IsConfigurationLocked { get; set; }

    public DateTime NextCheckAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual LegacyPlan LegacyPlan { get; set; } = null!;

    public virtual ICollection<ProofOfLifeCheckin> ProofOfLifeCheckins { get; set; } = new List<ProofOfLifeCheckin>();
}

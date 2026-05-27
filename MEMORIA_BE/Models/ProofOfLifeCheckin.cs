using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class ProofOfLifeCheckin
{
    public Guid CheckinId { get; set; }

    public Guid ScheduleId { get; set; }

    public DateTime SentAt { get; set; }

    public DateTime ResponseDeadline { get; set; }

    public DateTime? RespondedAt { get; set; }

    public string CheckinStatus { get; set; } = null!;

    public string Channel { get; set; } = null!;

    public string? FailureReason { get; set; }

    public virtual ProofOfLifeSchedule Schedule { get; set; } = null!;
}

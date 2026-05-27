using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class EmergencyReleaseEvent
{
    public Guid EmergencyReleaseId { get; set; }

    public Guid? TriggeredByUserId { get; set; }

    public string TriggerReason { get; set; } = null!;

    public string EventStatus { get; set; } = null!;

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? Note { get; set; }

    public virtual ICollection<EmergencyReleaseRecipient> EmergencyReleaseRecipients { get; set; } = new List<EmergencyReleaseRecipient>();

    public virtual User? TriggeredByUser { get; set; }
}

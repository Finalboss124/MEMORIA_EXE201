using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class FutureLetterRecipient
{
    public Guid RecipientId { get; set; }

    public Guid LetterId { get; set; }

    public string RecipientName { get; set; } = null!;

    public string? RecipientEmail { get; set; }

    public string? RecipientPhone { get; set; }

    public string? RecipientZalo { get; set; }

    public string? Relationship { get; set; }

    public string DeliveryChannel { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual FutureLetter Letter { get; set; } = null!;

    public virtual ICollection<ScheduledDeliveryLog> ScheduledDeliveryLogs { get; set; } = new List<ScheduledDeliveryLog>();
}

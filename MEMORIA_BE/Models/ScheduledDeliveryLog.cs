using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class ScheduledDeliveryLog
{
    public Guid DeliveryLogId { get; set; }

    public Guid LetterId { get; set; }

    public Guid RecipientId { get; set; }

    public string DeliveryStatus { get; set; } = null!;

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual FutureLetter Letter { get; set; } = null!;

    public virtual FutureLetterRecipient Recipient { get; set; } = null!;
}

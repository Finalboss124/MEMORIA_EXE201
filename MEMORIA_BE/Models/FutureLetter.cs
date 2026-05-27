using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class FutureLetter
{
    public Guid LetterId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string Title { get; set; } = null!;

    public string? BodyEncrypted { get; set; }

    public DateTime DeliveryDate { get; set; }

    public string SealStatus { get; set; } = null!;

    public bool IsLocked { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SealedAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public virtual ICollection<FutureLetterAttachment> FutureLetterAttachments { get; set; } = new List<FutureLetterAttachment>();

    public virtual ICollection<FutureLetterRecipient> FutureLetterRecipients { get; set; } = new List<FutureLetterRecipient>();

    public virtual User OwnerUser { get; set; } = null!;

    public virtual ICollection<ScheduledDeliveryLog> ScheduledDeliveryLogs { get; set; } = new List<ScheduledDeliveryLog>();
}

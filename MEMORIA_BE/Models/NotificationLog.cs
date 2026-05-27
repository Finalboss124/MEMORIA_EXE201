using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class NotificationLog
{
    public Guid NotificationId { get; set; }

    public Guid? UserId { get; set; }

    public string? RecipientEmail { get; set; }

    public string? RecipientPhone { get; set; }

    public string Channel { get; set; } = null!;

    public string? Subject { get; set; }

    public string Message { get; set; } = null!;

    public string SendStatus { get; set; } = null!;

    public DateTime? SentAt { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? User { get; set; }
}

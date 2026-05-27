using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class AuditLog
{
    public Guid AuditLogId { get; set; }

    public Guid? UserId { get; set; }

    public string ActionName { get; set; } = null!;

    public string? EntityName { get; set; }

    public string? EntityId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? User { get; set; }
}

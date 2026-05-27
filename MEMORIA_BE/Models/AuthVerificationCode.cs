using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class AuthVerificationCode
{
    public Guid VerificationId { get; set; }

    public Guid UserId { get; set; }

    public string CodeHash { get; set; } = null!;

    public string Purpose { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}

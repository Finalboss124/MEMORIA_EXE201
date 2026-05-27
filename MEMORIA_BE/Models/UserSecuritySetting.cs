using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class UserSecuritySetting
{
    public Guid UserId { get; set; }

    public bool IsTwoFactorEnabled { get; set; }

    public string? PreferredOtpChannel { get; set; }

    public string? RecoveryEmail { get; set; }

    public string? RecoveryPhone { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}

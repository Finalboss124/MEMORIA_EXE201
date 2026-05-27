using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class Guardian
{
    public Guid GuardianId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string Relationship { get; set; } = null!;

    public string VerificationStatus { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual User OwnerUser { get; set; } = null!;
}

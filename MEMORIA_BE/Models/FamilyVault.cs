using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class FamilyVault
{
    public Guid VaultId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string VaultName { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<FamilyVaultMember> FamilyVaultMembers { get; set; } = new List<FamilyVaultMember>();

    public virtual ICollection<Memory> Memories { get; set; } = new List<Memory>();

    public virtual User OwnerUser { get; set; } = null!;
}

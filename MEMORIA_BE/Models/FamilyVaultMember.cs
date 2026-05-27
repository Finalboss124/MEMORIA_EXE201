using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class FamilyVaultMember
{
    public Guid VaultMemberId { get; set; }

    public Guid VaultId { get; set; }

    public Guid? UserId { get; set; }

    public string? InviteEmail { get; set; }

    public string? MemberName { get; set; }

    public string MemberRole { get; set; } = null!;

    public string InviteStatus { get; set; } = null!;

    public DateTime InvitedAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public virtual User? User { get; set; }

    public virtual FamilyVault Vault { get; set; } = null!;
}

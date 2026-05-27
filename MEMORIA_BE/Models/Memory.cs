using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class Memory
{
    public Guid MemoryId { get; set; }

    public Guid VaultId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public DateOnly? MemoryDate { get; set; }

    public string? LocationName { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string Visibility { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User CreatedByUser { get; set; } = null!;

    public virtual ICollection<MemoryComment> MemoryComments { get; set; } = new List<MemoryComment>();

    public virtual ICollection<MemoryFile> MemoryFiles { get; set; } = new List<MemoryFile>();

    public virtual FamilyVault Vault { get; set; } = null!;
}

using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class DigitalAsset
{
    public Guid AssetId { get; set; }

    public Guid OwnerUserId { get; set; }

    public int CategoryId { get; set; }

    public string AssetName { get; set; } = null!;

    public string? AssetDescription { get; set; }

    public string? EncryptedSecret { get; set; }

    public Guid? FileId { get; set; }

    public string AssetStatus { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual DigitalAssetCategory Category { get; set; } = null!;

    public virtual StoredFile? File { get; set; }

    public virtual ICollection<LegacyPlanAsset> LegacyPlanAssets { get; set; } = new List<LegacyPlanAsset>();

    public virtual User OwnerUser { get; set; } = null!;
}

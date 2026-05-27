using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class DigitalAssetCategory
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<DigitalAsset> DigitalAssets { get; set; } = new List<DigitalAsset>();
}

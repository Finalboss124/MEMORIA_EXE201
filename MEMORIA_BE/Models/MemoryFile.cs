using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class MemoryFile
{
    public Guid MemoryFileId { get; set; }

    public Guid MemoryId { get; set; }

    public Guid FileId { get; set; }

    public string? Caption { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual StoredFile File { get; set; } = null!;

    public virtual Memory Memory { get; set; } = null!;
}

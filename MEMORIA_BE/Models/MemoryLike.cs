using System;

namespace MEMORIA_BE.Models;

public partial class MemoryLike
{
    public Guid MemoryLikeId { get; set; }

    public Guid MemoryId { get; set; }

    public Guid UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Memory Memory { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

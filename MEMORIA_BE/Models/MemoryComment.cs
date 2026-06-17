using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class MemoryComment
{
    public Guid CommentId { get; set; }

    public Guid MemoryId { get; set; }

    public Guid UserId { get; set; }

    public Guid? ParentCommentId { get; set; }

    public string CommentText { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public virtual Memory Memory { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

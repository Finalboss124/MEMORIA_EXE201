using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class FutureLetterAttachment
{
    public Guid LetterAttachmentId { get; set; }

    public Guid LetterId { get; set; }

    public Guid FileId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual StoredFile File { get; set; } = null!;

    public virtual FutureLetter Letter { get; set; } = null!;
}

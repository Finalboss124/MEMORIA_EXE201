using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class EmergencyReleaseRecipient
{
    public Guid EmergencyReleaseRecipientId { get; set; }

    public Guid EmergencyReleaseId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string RecipientName { get; set; } = null!;

    public string? RecipientEmail { get; set; }

    public string? RecipientPhone { get; set; }

    public string? RecipientZalo { get; set; }

    public Guid? ReleasePackageFileId { get; set; }

    public string DeliveryStatus { get; set; } = null!;

    public DateTime? SentAt { get; set; }

    public string? ErrorMessage { get; set; }

    public virtual EmergencyReleaseEvent EmergencyRelease { get; set; } = null!;

    public virtual User OwnerUser { get; set; } = null!;

    public virtual StoredFile? ReleasePackageFile { get; set; }
}

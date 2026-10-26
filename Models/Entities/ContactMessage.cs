using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class ContactMessage
{
    public long Id { get; set; }

    public long? UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string Subject { get; set; } = null!;

    public string Body { get; set; } = null!;

    public string Status { get; set; } = null!;

    public long? AssignedTo { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual User? AssignedToNavigation { get; set; }

    public virtual User? User { get; set; }
}

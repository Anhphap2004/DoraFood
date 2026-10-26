using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Notification
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    public string? LinkUrl { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual User User { get; set; } = null!;
}

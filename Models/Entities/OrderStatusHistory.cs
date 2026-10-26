using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class OrderStatusHistory
{
    public long Id { get; set; }

    public long OrderId { get; set; }

    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = null!;

    public long ChangedBy { get; set; }

    public string? Reason { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    public virtual User ChangedByNavigation { get; set; } = null!;

    public virtual Order Order { get; set; } = null!;
}

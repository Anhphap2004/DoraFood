using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class OrderTable
{
    public long Id { get; set; }

    public long OrderId { get; set; }

    public long TableId { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }

    public virtual Order Order { get; set; } = null!;

    public virtual DiningTable Table { get; set; } = null!;
}

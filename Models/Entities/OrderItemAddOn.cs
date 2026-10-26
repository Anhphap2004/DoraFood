using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class OrderItemAddOn
{
    public long Id { get; set; }

    public long OrderItemId { get; set; }

    public long AddOnId { get; set; }

    public string NameSnapshot { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public virtual AddOn AddOn { get; set; } = null!;

    public virtual OrderItem OrderItem { get; set; } = null!;
}

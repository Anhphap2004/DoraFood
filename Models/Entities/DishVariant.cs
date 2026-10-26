using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class DishVariant
{
    public long Id { get; set; }

    public long DishId { get; set; }

    public string Sku { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string SellingUnit { get; set; } = null!;

    public decimal Price { get; set; }

    public bool IsAvailable { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual Dish Dish { get; set; } = null!;

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    public virtual Recipe? Recipe { get; set; }
}

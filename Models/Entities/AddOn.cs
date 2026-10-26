using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class AddOn
{
    public long Id { get; set; }

    public string Name { get; set; } = null!;

    public decimal Price { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<AddOnRecipeItem> AddOnRecipeItems { get; set; } = new List<AddOnRecipeItem>();

    public virtual ICollection<OrderItemAddOn> OrderItemAddOns { get; set; } = new List<OrderItemAddOn>();

    public virtual ICollection<Dish> Dishes { get; set; } = new List<Dish>();
}

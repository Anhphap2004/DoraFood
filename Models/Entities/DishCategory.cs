using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class DishCategory
{
    public long Id { get; set; }

    public long? ParentId { get; set; }

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Dish> Dishes { get; set; } = new List<Dish>();

    public virtual ICollection<DishCategory> InverseParent { get; set; } = new List<DishCategory>();

    public virtual DishCategory? Parent { get; set; }
}

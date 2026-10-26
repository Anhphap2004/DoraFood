using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class DishImage
{
    public long Id { get; set; }

    public long DishId { get; set; }

    public long MediaFileId { get; set; }

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }

    public virtual Dish Dish { get; set; } = null!;

    public virtual MediaFile MediaFile { get; set; } = null!;
}

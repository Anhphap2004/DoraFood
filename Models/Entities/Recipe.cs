using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Recipe
{
    public long Id { get; set; }

    public long VariantId { get; set; }

    public int VersionNumber { get; set; }

    public decimal YieldQuantity { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual ICollection<RecipeItem> RecipeItems { get; set; } = new List<RecipeItem>();

    public virtual ICollection<StockDocumentLine> StockDocumentLines { get; set; } = new List<StockDocumentLine>();

    public virtual DishVariant Variant { get; set; } = null!;
}

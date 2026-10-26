using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Ingredient
{
    public long Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public long BaseUnitId { get; set; }

    public decimal ReorderLevel { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<AddOnRecipeItem> AddOnRecipeItems { get; set; } = new List<AddOnRecipeItem>();

    public virtual Unit BaseUnit { get; set; } = null!;

    public virtual ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();

    public virtual ICollection<RecipeItem> RecipeItems { get; set; } = new List<RecipeItem>();

    public virtual ICollection<StockCountLine> StockCountLines { get; set; } = new List<StockCountLine>();

    public virtual ICollection<StockDocumentLine> StockDocumentLines { get; set; } = new List<StockDocumentLine>();

    public virtual ICollection<StockLot> StockLots { get; set; } = new List<StockLot>();
}

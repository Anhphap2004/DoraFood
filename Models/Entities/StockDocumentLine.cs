using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class StockDocumentLine
{
    public long Id { get; set; }

    public long DocumentId { get; set; }

    public long IngredientId { get; set; }

    public long LotId { get; set; }

    public decimal QuantityDelta { get; set; }

    public decimal UnitCost { get; set; }

    public long? RecipeId { get; set; }

    public long? OrderItemId { get; set; }

    public string? Note { get; set; }

    public virtual StockDocument Document { get; set; } = null!;

    public virtual Ingredient Ingredient { get; set; } = null!;

    public virtual StockLot Lot { get; set; } = null!;

    public virtual OrderItem? OrderItem { get; set; }

    public virtual Recipe? Recipe { get; set; }

    public virtual StockLot StockLot { get; set; } = null!;
}

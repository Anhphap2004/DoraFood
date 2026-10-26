using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class StockCountLine
{
    public long Id { get; set; }

    public long StockCountId { get; set; }

    public long IngredientId { get; set; }

    public long LotId { get; set; }

    public decimal ExpectedQuantity { get; set; }

    public decimal ActualQuantity { get; set; }

    public decimal? Difference { get; set; }

    public virtual Ingredient Ingredient { get; set; } = null!;

    public virtual StockLot Lot { get; set; } = null!;

    public virtual StockCount StockCount { get; set; } = null!;

    public virtual StockLot StockLot { get; set; } = null!;
}

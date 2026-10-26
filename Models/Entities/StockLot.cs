using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class StockLot
{
    public long Id { get; set; }

    public long IngredientId { get; set; }

    public string LotCode { get; set; } = null!;

    public DateOnly? ManufacturedOn { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    public long? SupplierId { get; set; }

    public string? Note { get; set; }

    public virtual Ingredient Ingredient { get; set; } = null!;

    public virtual ICollection<StockCountLine> StockCountLineLots { get; set; } = new List<StockCountLine>();

    public virtual ICollection<StockCountLine> StockCountLineStockLots { get; set; } = new List<StockCountLine>();

    public virtual ICollection<StockDocumentLine> StockDocumentLineLots { get; set; } = new List<StockDocumentLine>();

    public virtual ICollection<StockDocumentLine> StockDocumentLineStockLots { get; set; } = new List<StockDocumentLine>();

    public virtual Supplier? Supplier { get; set; }
}

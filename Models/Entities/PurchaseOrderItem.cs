using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class PurchaseOrderItem
{
    public long Id { get; set; }

    public long PurchaseOrderId { get; set; }

    public long IngredientId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitCost { get; set; }

    public virtual Ingredient Ingredient { get; set; } = null!;

    public virtual PurchaseOrder PurchaseOrder { get; set; } = null!;
}

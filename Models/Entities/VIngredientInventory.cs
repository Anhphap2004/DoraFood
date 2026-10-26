using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class VIngredientInventory
{
    public long WarehouseId { get; set; }

    public long IngredientId { get; set; }

    public decimal? QuantityOnHand { get; set; }
}

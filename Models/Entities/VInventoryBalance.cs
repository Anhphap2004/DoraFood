using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class VInventoryBalance
{
    public long WarehouseId { get; set; }

    public long IngredientId { get; set; }

    public long LotId { get; set; }

    public decimal? QuantityOnHand { get; set; }
}

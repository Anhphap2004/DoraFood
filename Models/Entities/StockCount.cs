using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class StockCount
{
    public long Id { get; set; }

    public string Number { get; set; } = null!;

    public long WarehouseId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CountedAtUtc { get; set; }

    public long? AdjustmentDocumentId { get; set; }

    public long CreatedBy { get; set; }

    public string? Note { get; set; }

    public virtual StockDocument? AdjustmentDocument { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<StockCountLine> StockCountLines { get; set; } = new List<StockCountLine>();

    public virtual Warehouse Warehouse { get; set; } = null!;
}

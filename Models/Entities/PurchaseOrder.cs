using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class PurchaseOrder
{
    public long Id { get; set; }

    public string Number { get; set; } = null!;

    public long SupplierId { get; set; }

    public long WarehouseId { get; set; }

    public string Status { get; set; } = null!;

    public DateOnly? ExpectedOn { get; set; }

    public long CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();

    public virtual ICollection<StockDocument> StockDocuments { get; set; } = new List<StockDocument>();

    public virtual Supplier Supplier { get; set; } = null!;

    public virtual Warehouse Warehouse { get; set; } = null!;
}

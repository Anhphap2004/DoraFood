using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Warehouse
{
    public long Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; }

    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();

    public virtual ICollection<StockCount> StockCounts { get; set; } = new List<StockCount>();

    public virtual ICollection<StockDocument> StockDocuments { get; set; } = new List<StockDocument>();
}

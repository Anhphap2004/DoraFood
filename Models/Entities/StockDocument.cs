using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class StockDocument
{
    public long Id { get; set; }

    public string Number { get; set; } = null!;

    public long WarehouseId { get; set; }

    public string DocumentType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public long? PurchaseOrderId { get; set; }

    public long? OrderId { get; set; }

    public long? ReversesDocumentId { get; set; }

    public Guid IdempotencyKey { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string? Note { get; set; }

    public long CreatedBy { get; set; }

    public long? PostedBy { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual StockDocument? InverseReversesDocument { get; set; }

    public virtual Order? Order { get; set; }

    public virtual User? PostedByNavigation { get; set; }

    public virtual PurchaseOrder? PurchaseOrder { get; set; }

    public virtual StockDocument? ReversesDocument { get; set; }

    public virtual StockCount? StockCount { get; set; }

    public virtual ICollection<StockDocumentLine> StockDocumentLines { get; set; } = new List<StockDocumentLine>();

    public virtual Warehouse Warehouse { get; set; } = null!;
}

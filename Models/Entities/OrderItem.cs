using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class OrderItem
{
    public long Id { get; set; }

    public long OrderId { get; set; }

    public long VariantId { get; set; }

    public string NameSnapshot { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public string Status { get; set; } = null!;

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual ICollection<InvoiceLine> InvoiceLines { get; set; } = new List<InvoiceLine>();

    public virtual Order Order { get; set; } = null!;

    public virtual ICollection<OrderItemAddOn> OrderItemAddOns { get; set; } = new List<OrderItemAddOn>();

    public virtual ICollection<StockDocumentLine> StockDocumentLines { get; set; } = new List<StockDocumentLine>();

    public virtual DishVariant Variant { get; set; } = null!;
}

using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class InvoiceLine
{
    public long Id { get; set; }

    public long InvoiceId { get; set; }

    public int LineNumber { get; set; }

    public string LineType { get; set; } = null!;

    public long? OrderItemId { get; set; }

    public string Description { get; set; } = null!;

    public string Unit { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxRate { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal? LineSubtotal { get; set; }

    public decimal? LineTotal { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;

    public virtual OrderItem? OrderItem { get; set; }
}

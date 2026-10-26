using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Invoice
{
    public long Id { get; set; }

    public long OrderId { get; set; }

    public string Number { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? CustomerName { get; set; }

    public string? CustomerTaxCode { get; set; }

    public string? CustomerAddress { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal? GrandTotal { get; set; }

    public DateTime? IssuedAtUtc { get; set; }

    public long IssuedBy { get; set; }

    public string? ExternalInvoiceRef { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual ICollection<InvoiceLine> InvoiceLines { get; set; } = new List<InvoiceLine>();

    public virtual User IssuedByNavigation { get; set; } = null!;

    public virtual Order Order { get; set; } = null!;
}

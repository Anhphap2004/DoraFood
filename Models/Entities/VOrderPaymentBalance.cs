using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class VOrderPaymentBalance
{
    public long OrderId { get; set; }

    public long? InvoiceId { get; set; }

    public string? InvoiceStatus { get; set; }

    public decimal? GrandTotal { get; set; }

    public decimal? PaidAmount { get; set; }

    public decimal? RefundedAmount { get; set; }

    public decimal? NetPaid { get; set; }

    public decimal? BalanceDue { get; set; }
}

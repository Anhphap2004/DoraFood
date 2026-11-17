using System;
using System.Collections.Generic;
using DoraFood.Models;

namespace DoraFood.ViewModels;

public sealed class MyBookingInvoiceViewModel
{
    public Invoice Invoice { get; init; } = null!;

    public long ReservationId { get; init; }

    public string ReservationCode { get; init; } = string.Empty;

    public string OrderCode { get; init; } = string.Empty;

    public IReadOnlyList<string> TableCodes { get; init; }
        = Array.Empty<string>();

    public decimal DepositAmount { get; init; }

    public decimal SettlementAmount { get; init; }

    public decimal RefundedAmount { get; init; }

    public decimal TotalAmount =>
        Invoice.Subtotal
        - Invoice.DiscountAmount
        + Invoice.TaxAmount;

    public decimal NetReceivedAmount =>
        DepositAmount + SettlementAmount - RefundedAmount;

    public decimal RemainingAmount =>
        Math.Max(0m, TotalAmount - NetReceivedAmount);
}
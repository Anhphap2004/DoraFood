using System;
using System.Collections.Generic;
using DoraFood.Models;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class TableInvoiceViewModel
{
    public Invoice Invoice { get; init; } = null!;

    public string OrderCode { get; init; } = string.Empty;

    public string CashierName { get; init; } = string.Empty;

    public IReadOnlyList<string> TableCodes { get; init; }
        = Array.Empty<string>();

    // Các khoản tiền tính đến thời điểm xuất hóa đơn.
    public decimal DepositAmount { get; init; }

    public decimal SettlementAmount { get; init; }

    public decimal RefundedAmount { get; init; }

    public decimal NetReceivedAmount =>
        DepositAmount + SettlementAmount - RefundedAmount;

    public decimal TotalAmount =>
        Invoice.Subtotal
        - Invoice.DiscountAmount
        + Invoice.TaxAmount;

    public decimal RemainingAmount =>
        Math.Max(0m, TotalAmount - NetReceivedAmount);
}
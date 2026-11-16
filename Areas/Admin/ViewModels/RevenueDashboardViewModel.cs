using System;
using System.Collections.Generic;
using System.Linq;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class RevenueDashboardViewModel
{
    // Khoảng ngày hiển thị theo giờ Việt Nam.
    public DateTime FromDate { get; init; }

    public DateTime ToDate { get; init; }

    public IReadOnlyList<RevenueDailyRow> Days { get; init; }
        = Array.Empty<RevenueDailyRow>();

    // Tổng tiền thanh toán thành công trong kỳ.
    public decimal CollectedAmount =>
        Days.Sum(day => day.CollectedAmount);

    // Tổng tiền hoàn thành công trong kỳ.
    public decimal RefundedAmount =>
        Days.Sum(day => day.RefundedAmount);

    // Không trừ thêm voucher ở đây.
    public decimal NetCollectedAmount =>
        CollectedAmount - RefundedAmount;

    // Đơn chuyển sang hoàn tất trong kỳ.
    public long CompletedOrders =>
        Days.Sum(day => day.CompletedOrders);

    // Khoản giảm của các đơn hoàn tất trong kỳ.
    public decimal DiscountAmount =>
        Days.Sum(day => day.DiscountAmount);
}

public sealed record RevenueDailyRow(
    DateTime Date,
    decimal CollectedAmount,
    decimal RefundedAmount,
    long CompletedOrders,
    decimal DiscountAmount)
{
    public decimal NetCollectedAmount =>
        CollectedAmount - RefundedAmount;
}
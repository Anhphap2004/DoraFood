using System;
using System.Collections.Generic;
using System.Linq;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class TableCheckoutViewModel
{
    public long OrderId { get; init; }

    public string OrderCode { get; init; } = "";

    public string OrderStatus { get; init; } = "";

    public long? ReservationId { get; init; }

    public string? ReservationCode { get; init; }

    public string? GuestName { get; init; }

    public string? GuestPhone { get; init; }

    public IReadOnlyList<string> Tables { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<TableCheckoutLineRow> Lines { get; init; }
        = Array.Empty<TableCheckoutLineRow>();

    public IReadOnlyList<TableCheckoutDiscountRow> Discounts { get; init; }
        = Array.Empty<TableCheckoutDiscountRow>();

    // Tổng tiền các dòng món, topping và dịch vụ.
    public decimal Subtotal =>
        Lines.Sum(line => line.LineTotal);

    public decimal DiscountAmount =>
    Discounts.Sum(item => item.Amount)
    + PreviewVoucherDiscount;

    public decimal TaxAmount { get; init; }

    public decimal TotalAmount =>
        Subtotal - DiscountAmount + TaxAmount;

    // Bao gồm tiền cọc và các khoản thanh toán đã thu thành công.
    public decimal ReceivedAmount { get; init; }

    // Khoản đã hoàn thành công của các lần thu trên đơn.
    public decimal RefundedAmount { get; init; }

    public decimal NetReceivedAmount =>
        ReceivedAmount - RefundedAmount;

    // Số tiền chưa được thanh toán.
    public decimal Balance =>
        TotalAmount - NetReceivedAmount;

    public decimal AmountToCollect =>
        Math.Max(0m, Balance);

    public decimal OverpaidAmount =>
        Math.Max(0m, -Balance);

    // Do controller xác định từ trạng thái đơn và dữ liệu thanh toán.
    public bool CanCheckout { get; set; }

    // Bảo vệ thông tin của lần kiểm tra thanh toán.
    public string? ConfirmationToken { get; set; }
    // Mã đang nhập trên trang thanh toán.
    public string? VoucherCode { get; set; }

    // Thông báo lỗi của voucher.
    public string? VoucherError { get; set; }

    // Khoản giảm đang xem trước, chưa lưu vào database.
    public decimal PreviewVoucherDiscount { get; set; }

    // Đơn đã có voucher được lưu từ trước.
    public bool HasSavedVoucher { get; set; }
}

public sealed record TableCheckoutLineRow(
    string LineType,
    long? OrderItemId,
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitPrice)
{
    // Khớp cách làm tròn thành tiền từng dòng trong SQL.
    public decimal LineTotal =>
        decimal.Round(
            Quantity * UnitPrice,
            2,
            MidpointRounding.AwayFromZero);
}

public sealed record TableCheckoutDiscountRow(
    string Description,
    decimal Amount);
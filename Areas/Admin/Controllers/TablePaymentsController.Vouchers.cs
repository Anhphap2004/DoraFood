using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public partial class TablePaymentsController
{
    // Kết quả kiểm tra voucher.
    private sealed record TableVoucherResult(
        bool IsValid,
        string? ErrorMessage,
        long? VoucherId,
        string? Code,
        decimal DiscountAmount);

    private static TableVoucherResult InvalidTableVoucher(
        string message)
    {
        return new TableVoucherResult(
            false,
            message,
            null,
            null,
            0m);
    }

    // Chỉ kiểm tra và tính giảm giá.
    // Chưa lưu OrderDiscount, chưa thu tiền.
    private async Task<TableVoucherResult> CheckTableVoucherAsync(
        string? code,
        decimal subtotal,
        long orderId,
        long? customerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new TableVoucherResult(
                true,
                null,
                null,
                null,
                0m);
        }

        code = code.Trim().ToUpperInvariant();

        if (code.Length > 30)
        {
            return InvalidTableVoucher(
                "Mã giảm giá tối đa 30 ký tự.");
        }

        if (subtotal <= 0)
        {
            return InvalidTableVoucher(
                "Đơn chưa có tiền món hoặc dịch vụ để áp mã.");
        }

        var voucher = await _context.Set<Voucher>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Code == code,
                cancellationToken);

        if (voucher == null)
        {
            return InvalidTableVoucher(
                "Mã giảm giá không tồn tại.");
        }

        if (!voucher.IsActive)
        {
            return InvalidTableVoucher(
                "Mã giảm giá đã ngừng hoạt động.");
        }

        var now = DateTime.UtcNow;

        if (now < voucher.StartsAtUtc)
        {
            return InvalidTableVoucher(
                "Mã giảm giá chưa đến thời gian sử dụng.");
        }

        if (now >= voucher.EndsAtUtc)
        {
            return InvalidTableVoucher(
                "Mã giảm giá đã hết hạn.");
        }

        if (subtotal < voucher.MinimumOrder)
        {
            return InvalidTableVoucher(
                $"Đơn cần đạt tối thiểu " +
                $"{voucher.MinimumOrder:N0} ₫ để dùng mã này.");
        }

        // Đếm theo số đơn, không đếm số dòng giảm giá.
        // Đơn đã hủy không chiếm lượt sử dụng.
        // Loại đơn hiện tại để có thể kiểm tra lại mã đã áp.
        var usages = _context.Set<OrderDiscount>()
            .AsNoTracking()
            .Where(discount =>
                discount.VoucherId == voucher.Id
                && discount.OrderId != orderId
                && discount.Order.Status != "CANCELLED");

        if (voucher.UsageLimit.HasValue)
        {
            var usedCount = await usages
                .Select(discount => discount.OrderId)
                .Distinct()
                .CountAsync(cancellationToken);

            if (usedCount >= voucher.UsageLimit.Value)
            {
                return InvalidTableVoucher(
                    "Mã giảm giá đã hết lượt sử dụng.");
            }
        }

        if (voucher.PerCustomerLimit.HasValue)
        {
            if (!customerId.HasValue || customerId.Value <= 0)
            {
                return InvalidTableVoucher(
                    "Mã này giới hạn theo tài khoản. " +
                    "Đơn cần liên kết với tài khoản khách hàng.");
            }

            var customerUsedCount = await usages
                .Where(discount =>
                    discount.Order.CustomerId == customerId.Value)
                .Select(discount => discount.OrderId)
                .Distinct()
                .CountAsync(cancellationToken);

            if (customerUsedCount >= voucher.PerCustomerLimit.Value)
            {
                return InvalidTableVoucher(
                    "Khách hàng đã dùng hết lượt của mã này.");
            }
        }

        decimal discountAmount;

        switch (voucher.DiscountType)
        {
            case "PERCENT":
                {
                    if (voucher.DiscountValue <= 0
                        || voucher.DiscountValue > 100)
                    {
                        return InvalidTableVoucher(
                            "Tỷ lệ giảm giá của voucher không hợp lệ.");
                    }

                    discountAmount =
                        subtotal * voucher.DiscountValue / 100m;

                    if (voucher.MaxDiscount.HasValue)
                    {
                        if (voucher.MaxDiscount.Value <= 0)
                        {
                            return InvalidTableVoucher(
                                "Mức giảm tối đa của voucher không hợp lệ.");
                        }

                        discountAmount = Math.Min(
                            discountAmount,
                            voucher.MaxDiscount.Value);
                    }

                    break;
                }

            case "FIXED":
                {
                    if (voucher.DiscountValue <= 0)
                    {
                        return InvalidTableVoucher(
                            "Số tiền giảm của voucher không hợp lệ.");
                    }

                    discountAmount = voucher.DiscountValue;
                    break;
                }

            default:
                return InvalidTableVoucher(
                    "Loại giảm giá chưa được hỗ trợ.");
        }

        // Không giảm vượt tiền món và dịch vụ.
        discountAmount = Math.Min(discountAmount, subtotal);

        // Làm tròn xuống đến đồng.
        discountAmount = decimal.Floor(discountAmount);

        if (discountAmount <= 0)
        {
            return InvalidTableVoucher(
                "Mã này không tạo ra khoản giảm giá hợp lệ.");
        }

        return new TableVoucherResult(
            true,
            null,
            voucher.Id,
            voucher.Code,
            discountAmount);
    }
}
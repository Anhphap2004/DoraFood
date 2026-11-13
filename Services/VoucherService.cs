using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Services;

public class VoucherService
{
    private readonly DoraFoodContext _context;

    public VoucherService(DoraFoodContext context)
    {
        _context = context;
    }

    public async Task<VoucherCheckResult> CheckAsync(
        string? code,
        long customerId,
        decimal subtotal,
        CancellationToken cancellationToken = default)
    {
        VoucherCheckResult Invalid(string message) =>
            new(false, message, null, null, 0m, subtotal);

        // 1. Kiểm tra dữ liệu đầu vào.
        if (customerId <= 0)
        {
            return Invalid("Vui lòng đăng nhập để sử dụng mã giảm giá.");
        }

        if (subtotal <= 0m
            || subtotal > 99999999999999999.99m
            || subtotal != decimal.Truncate(subtotal))
        {
            return Invalid("Tổng tiền món không hợp lệ.");
        }

        // Không nhập mã vẫn được đặt hàng bình thường.
        if (string.IsNullOrWhiteSpace(code))
        {
            return new VoucherCheckResult(
                true,
                null,
                null,
                null,
                0m,
                subtotal);
        }

        code = code.Trim().ToUpperInvariant();

        if (code.Length > 50)
        {
            return Invalid("Mã giảm giá tối đa 50 ký tự.");
        }

        // 2. Đọc mã từ database.
        var voucher = await _context.Set<Voucher>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Code == code,
                cancellationToken);

        if (voucher is null)
        {
            return Invalid("Mã giảm giá không tồn tại.");
        }

        if (!voucher.IsActive)
        {
            return Invalid("Mã giảm giá đã ngừng sử dụng.");
        }

        // 3. Kiểm tra thời hạn.
        var now = DateTime.UtcNow;

        if (now < voucher.StartsAtUtc)
        {
            return Invalid("Mã giảm giá chưa đến thời gian áp dụng.");
        }

        if (now >= voucher.EndsAtUtc)
        {
            return Invalid("Mã giảm giá đã hết hạn.");
        }

        // 4. Kiểm tra giá trị đơn tối thiểu.
        if (subtotal < voucher.MinimumOrder)
        {
            return Invalid(
                $"Mã này áp dụng cho đơn từ "
                + $"{voucher.MinimumOrder:N0} ₫.");
        }

        // Mỗi đơn chỉ được tính một lượt, dù có nhiều dòng giảm giá.
        var usedOrders = _context.Set<Order>()
            .AsNoTracking()
            .Where(order =>
                order.Status != "CANCELLED"
                && order.OrderDiscounts.Any(
                    discount => discount.VoucherId == voucher.Id));

        // 5. Kiểm tra tổng lượt sử dụng.
        if (voucher.UsageLimit.HasValue)
        {
            var totalUses = await usedOrders
                .LongCountAsync(cancellationToken);

            if (totalUses >= voucher.UsageLimit.Value)
            {
                return Invalid("Mã giảm giá đã hết lượt sử dụng.");
            }
        }

        // 6. Kiểm tra số lượt của khách hiện tại.
        if (voucher.PerCustomerLimit.HasValue)
        {
            var customerUses = await usedOrders
                .LongCountAsync(
                    order => order.CustomerId == customerId,
                    cancellationToken);

            if (customerUses >= voucher.PerCustomerLimit.Value)
            {
                return Invalid(
                    "Anh đã sử dụng hết số lượt cho phép của mã này.");
            }
        }

        // 7. Tính số tiền được giảm.
        if (voucher.DiscountValue <= 0m)
        {
            return Invalid("Cấu hình mã giảm giá không hợp lệ.");
        }

        decimal discount;

        switch (voucher.DiscountType)
        {
            case "PERCENT":
                if (voucher.DiscountValue > 100m)
                {
                    return Invalid(
                        "Cấu hình phần trăm giảm giá không hợp lệ.");
                }

                discount = subtotal * voucher.DiscountValue / 100m;

                if (voucher.MaxDiscount.HasValue)
                {
                    if (voucher.MaxDiscount.Value <= 0m)
                    {
                        return Invalid(
                            "Cấu hình mức giảm tối đa không hợp lệ.");
                    }

                    discount = Math.Min(
                        discount,
                        voucher.MaxDiscount.Value);
                }

                break;

            case "FIXED":
                discount = voucher.DiscountValue;
                break;

            default:
                return Invalid("Hình thức giảm giá không hợp lệ.");
        }

        // Không giảm vượt tiền món, làm tròn xuống đến đồng.
        discount = decimal.Floor(Math.Min(discount, subtotal));

        if (discount <= 0m)
        {
            return Invalid(
                "Mức giảm của mã này chưa đủ 1 đồng cho đơn hiện tại.");
        }

        var totalAmount = subtotal - discount;

        // Luồng hiện tại tạo khoản tiền mặt chờ thu có số tiền dương.
        if (totalAmount <= 0m)
        {
            return Invalid(
                "Mã này giảm hết tiền món. "
                + "Hiện hệ thống chưa hỗ trợ đơn có số tiền thanh toán bằng 0.");
        }

        return new VoucherCheckResult(
            true,
            null,
            voucher.Id,
            voucher.Code,
            discount,
            totalAmount);
    }
}

// Kết quả trả về cho controller.
public sealed record VoucherCheckResult(
    bool IsValid,
    string? ErrorMessage,
    long? VoucherId,
    string? Code,
    decimal DiscountAmount,
    decimal TotalAmount);
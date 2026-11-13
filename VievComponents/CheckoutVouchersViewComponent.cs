using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.ViewComponents;

public sealed class CheckoutVouchersViewComponent : ViewComponent
{
    private readonly DoraFoodContext _context;

    public CheckoutVouchersViewComponent(DoraFoodContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync(decimal subtotal)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true
            || !long.TryParse(
                HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId)
            || userId <= 0)
        {
            return Content(string.Empty);
        }

        var cancellationToken = HttpContext.RequestAborted;
        var now = DateTime.UtcNow;

        var usedOrders = _context.Set<Order>()
            .AsNoTracking()
            .Where(order => order.Status != "CANCELLED");

        // Lấy những mã còn thời hạn và lượt sử dụng.
        var vouchers = await _context.Set<Voucher>()
            .AsNoTracking()
            .Where(voucher =>
                voucher.IsActive
                && voucher.StartsAtUtc <= now
                && voucher.EndsAtUtc > now)
            .Select(voucher => new
            {
                Voucher = voucher,

                TotalUsed = usedOrders.LongCount(order =>
                    order.OrderDiscounts.Any(discount =>
                        discount.VoucherId == voucher.Id)),

                UsedByCustomer = usedOrders.LongCount(order =>
                    order.CustomerId == userId
                    && order.OrderDiscounts.Any(discount =>
                        discount.VoucherId == voucher.Id))
            })
            .Where(item =>
                (!item.Voucher.UsageLimit.HasValue
                    || item.TotalUsed < item.Voucher.UsageLimit.Value)
                && (!item.Voucher.PerCustomerLimit.HasValue
                    || item.UsedByCustomer
                        < item.Voucher.PerCustomerLimit.Value))
            .Select(item => new MyVoucherRow
            {
                Code = item.Voucher.Code,
                Name = item.Voucher.Name,
                DiscountType = item.Voucher.DiscountType,
                DiscountValue = item.Voucher.DiscountValue,
                MaxDiscount = item.Voucher.MaxDiscount,
                MinimumOrder = item.Voucher.MinimumOrder,
                EndsAtUtc = item.Voucher.EndsAtUtc,

                ImageUrl = item.Voucher.Cover != null
                    ? item.Voucher.Cover.Url
                    : null,

                ImageAlt = item.Voucher.Cover != null
                    ? item.Voucher.Cover.AltText
                    : null,

                UsageLimit = item.Voucher.UsageLimit,
                PerCustomerLimit = item.Voucher.PerCustomerLimit,
                TotalUsed = item.TotalUsed,
                UsedByCustomer = item.UsedByCustomer
            })
            .ToListAsync(cancellationToken);

        // Tính trên toàn bộ danh sách trước khi lấy 20 mã hiển thị.
        var options = vouchers
            .Select(voucher => BuildOption(voucher, subtotal))
            .OrderByDescending(option => option.CanUse)
            .ThenByDescending(option => option.EstimatedDiscount)
            .ThenBy(option => option.Voucher.EndsAtUtc)
            .ThenBy(option => option.Voucher.Code)
            .Take(20)
            .ToList();

        if (options.Count > 0 && options[0].CanUse)
        {
            options[0] = options[0] with
            {
                IsBestChoice = true
            };
        }

        ViewData["Subtotal"] = subtotal;

        return View(options);
    }

    private static VoucherOption BuildOption(
        MyVoucherRow voucher,
        decimal subtotal)
    {
        if (subtotal < voucher.MinimumOrder)
        {
            return new VoucherOption(
                voucher,
                0m,
                $"Đơn cần đạt tối thiểu {voucher.MinimumOrder:N0} ₫.");
        }

        if (subtotal <= 0m)
        {
            return new VoucherOption(
                voucher,
                0m,
                "Giỏ hàng chưa có giá trị thanh toán hợp lệ.");
        }

        var discount = voucher.DiscountType == "PERCENT"
            ? subtotal * voucher.DiscountValue / 100m
            : voucher.DiscountValue;

        if (voucher.DiscountType == "PERCENT"
            && voucher.MaxDiscount.HasValue)
        {
            discount = Math.Min(
                discount,
                voucher.MaxDiscount.Value);
        }

        discount = decimal.Floor(Math.Min(discount, subtotal));

        if (discount <= 0m)
        {
            return new VoucherOption(
                voucher,
                0m,
                "Mức giảm của đơn hiện tại chưa đủ 1 ₫.");
        }

        if (discount >= subtotal)
        {
            return new VoucherOption(
                voucher,
                0m,
                "Mã này chưa phù hợp: đơn sau giảm cần còn tiền thanh toán.");
        }

        return new VoucherOption(voucher, discount, null);
    }

    public sealed record VoucherOption(
        MyVoucherRow Voucher,
        decimal EstimatedDiscount,
        string? UnavailableReason,
        bool IsBestChoice = false)
    {
        public bool CanUse => UnavailableReason is null;
    }
}
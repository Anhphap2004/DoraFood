using System;
using System.Linq;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.ViewComponents;

public sealed class VoucherPromotionsViewComponent : ViewComponent
{
    private readonly DoraFoodContext _context;

    public VoucherPromotionsViewComponent(DoraFoodContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var now = DateTime.UtcNow;
        var cancellationToken = HttpContext.RequestAborted;

        var vouchers = await _context.Set<Voucher>()
            .AsNoTracking()
            .Where(voucher =>
                voucher.IsActive
                && voucher.StartsAtUtc <= now
                && voucher.EndsAtUtc > now
                && voucher.Cover != null
                && voucher.Cover.Url.Trim() != "")
            .Where(voucher =>
                !voucher.UsageLimit.HasValue
                || _context.Set<Order>()
                    .LongCount(order =>
                        order.Status != "CANCELLED"
                        && order.OrderDiscounts.Any(discount =>
                            discount.VoucherId == voucher.Id))
                    < voucher.UsageLimit.Value)
            .OrderBy(voucher => voucher.EndsAtUtc)
            .ThenByDescending(voucher => voucher.Id)
            .Take(6)
            .Select(voucher => new VoucherPromotionItem(
                voucher.Code,
                voucher.Name,
                voucher.DiscountType,
                voucher.DiscountValue,
                voucher.MaxDiscount,
                voucher.MinimumOrder,
                voucher.PerCustomerLimit,
                voucher.EndsAtUtc,
                voucher.Cover!.Url,
                voucher.Cover!.AltText))
            .ToListAsync(cancellationToken);

        if (vouchers.Count == 0)
        {
            return Content(string.Empty);
        }

        return View(vouchers);
    }
}
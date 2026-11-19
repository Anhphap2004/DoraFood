using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public sealed class OffersController : Controller
{
    private readonly DoraFoodContext _context;

    public OffersController(DoraFoodContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpGet("/uu-dai", Name = "DoraFoodVouchers")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

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
            .Select(voucher => new PublicVoucherViewModel(
                voucher.Code,
                voucher.Name,
                voucher.DiscountType,
                voucher.DiscountValue,
                voucher.MaxDiscount,
                voucher.MinimumOrder,
                voucher.PerCustomerLimit,
                voucher.EndsAtUtc,
                voucher.Cover!.Url))
            .ToListAsync(cancellationToken);

        return View(vouchers);
    }
}

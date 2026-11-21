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
    public async Task<IActionResult> Index([FromQuery] string? filter, CancellationToken cancellationToken)
    {
        var selectedFilter = filter?.Trim().ToLowerInvariant() switch
        {
            "con-luot" => "con-luot",
            "het-han" => "het-han",
            _ => "moi-nhat"
        };

        var now = DateTime.UtcNow;
        var expired = selectedFilter == "het-han";

        var query = _context.Set<Voucher>()
            .AsNoTracking()
            .Where(voucher =>
                voucher.IsActive
                && voucher.StartsAtUtc <= now
                && voucher.Cover != null
                && voucher.Cover.Url.Trim() != "")
            .Where(voucher => expired ? voucher.EndsAtUtc <= now : voucher.EndsAtUtc > now)
            .Select(voucher => new
            {
                Voucher = voucher,
                UsedCount = !expired && voucher.UsageLimit.HasValue
                    ? _context.Set<Order>()
                        .LongCount(order =>
                            order.Status != "CANCELLED"
                            && order.OrderDiscounts.Any(discount => discount.VoucherId == voucher.Id))
                    : 0L
            });

        if (!expired)
        {
            query = query.Where(row => !row.Voucher.UsageLimit.HasValue
                || row.UsedCount < row.Voucher.UsageLimit.Value);
        }

        var ordered = selectedFilter switch
        {
            "het-han" => query.OrderByDescending(row => row.Voucher.EndsAtUtc)
                .ThenByDescending(row => row.Voucher.Id),
            "con-luot" => query.OrderBy(row => row.Voucher.EndsAtUtc)
                .ThenByDescending(row => row.Voucher.Id),
            _ => query.OrderByDescending(row => row.Voucher.Id)
        };

        var vouchers = await ordered
            .Select(row => new PublicVoucherViewModel(
                expired ? string.Empty : row.Voucher.Code,
                row.Voucher.Name,
                row.Voucher.DiscountType,
                row.Voucher.DiscountValue,
                row.Voucher.MaxDiscount,
                row.Voucher.MinimumOrder,
                row.Voucher.PerCustomerLimit,
                row.Voucher.EndsAtUtc,
                row.Voucher.Cover!.Url,
                expired ? null : row.Voucher.UsageLimit,
                row.UsedCount))
            .ToListAsync(cancellationToken);

        return View(new PublicVoucherPageViewModel(selectedFilter, vouchers));
    }
}

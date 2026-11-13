using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

[Authorize]
[Route("tai-khoan/uu-dai")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileVouchersController : Controller
{
    private const int PageSize = 9;

    private readonly DoraFoodContext _context;

    public ProfileVouchersController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet(Name = "DoraFood.MyVouchers")]
    public async Task<IActionResult> Index(
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Số trang không hợp lệ.");
        }

        var userId = await GetActiveUserIdAsync(cancellationToken);

        if (!userId.HasValue)
        {
            return Forbid();
        }

        var customerId = userId.Value;
        var now = DateTime.UtcNow;

        // Đơn đã hủy không chiếm lượt sử dụng.
        var usedOrders = _context.Set<Order>()
            .AsNoTracking()
            .Where(order => order.Status != "CANCELLED");

        var query = _context.Set<Voucher>()
            .AsNoTracking()
            .Where(voucher =>
                voucher.IsActive
                && voucher.StartsAtUtc <= now
                && voucher.EndsAtUtc > now)
            .Select(voucher => new
            {
                Voucher = voucher,

                // Đếm theo đơn, không đếm số dòng OrderDiscount.
                TotalUsed = usedOrders.LongCount(order =>
                    order.OrderDiscounts.Any(discount =>
                        discount.VoucherId == voucher.Id)),

                UsedByCustomer = usedOrders.LongCount(order =>
                    order.CustomerId == customerId
                    && order.OrderDiscounts.Any(discount =>
                        discount.VoucherId == voucher.Id))
            })
            .Where(item =>
                (!item.Voucher.UsageLimit.HasValue
                    || item.TotalUsed < item.Voucher.UsageLimit.Value)
                && (!item.Voucher.PerCustomerLimit.HasValue
                    || item.UsedByCustomer
                        < item.Voucher.PerCustomerLimit.Value));

        var totalVouchers = await query.CountAsync(cancellationToken);

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalVouchers / (double)PageSize));

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var vouchers = await query
            .OrderBy(item => item.Voucher.EndsAtUtc)
            .ThenByDescending(item => item.Voucher.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
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

        ViewData["Title"] = "Ưu đãi của tôi";

        return View(new MyVouchersViewModel
        {
            Vouchers = vouchers,
            PageNumber = pageNumber,
            TotalPages = totalPages,
            TotalVouchers = totalVouchers
        });
    }

    private async Task<long?> GetActiveUserIdAsync(
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            || userId <= 0)
        {
            return null;
        }

        var isActive = await _context.Set<DoraFood.Models.User>()
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId && user.IsActive,
                cancellationToken);

        return isActive ? userId : null;
    }
}
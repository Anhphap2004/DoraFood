using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[Route("admin/overview")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminOverviewController : AdminModuleController
{
    public AdminOverviewController(DoraFoodContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken,
        DateOnly? from = null, DateOnly? to = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;
        if (!ModelState.IsValid || start.Year < 1900 || start > end || end > today || end.DayNumber - start.DayNumber > 365)
            return BadRequest("Chọn khoảng ngày từ năm 1900, tối đa 366 ngày và không vượt quá hôm nay.");

        var startUtc = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).UtcDateTime;
        var endUtc = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).UtcDateTime;

        // Thực thu theo ngày nhận tiền và hoàn tiền theo ngày hoàn thành công.
        var cash = await Db.Database.SqlQuery<DailyCashRow>($"""
            SELECT CAST(DATEADD(hour, 7, x.EventAtUtc) AS date) AS [Day],
                SUM(x.Received) AS Received, SUM(x.Refunded) AS Refunded
            FROM
            (
                SELECT PaidAtUtc AS EventAtUtc, Amount AS Received,
                    CAST(0 AS decimal(19,2)) AS Refunded
                FROM dbo.Payments
                WHERE Status = 'SUCCEEDED' AND PaidAtUtc >= {startUtc} AND PaidAtUtc < {endUtc}
                UNION ALL
                SELECT RefundedAtUtc, CAST(0 AS decimal(19,2)), Amount
                FROM dbo.Refunds
                WHERE Status = 'SUCCEEDED' AND RefundedAtUtc >= {startUtc} AND RefundedAtUtc < {endUtc}
            ) AS x
            GROUP BY CAST(DATEADD(hour, 7, x.EventAtUtc) AS date)
            """).ToListAsync(cancellationToken);

        var cashByDay = cash.ToDictionary(day => day.Day.Date);
        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1)
            .Select(offset => start.AddDays(offset).ToDateTime(TimeOnly.MinValue))
            .Select(day => cashByDay.TryGetValue(day, out var row)
                ? row : new DailyCashRow(day, 0m, 0m)).ToList();

        var orders = Db.Set<Order>().AsNoTracking()
            .Where(order => order.OpenedAtUtc >= startUtc && order.OpenedAtUtc < endUtc);
        var totalOrders = await orders.CountAsync(cancellationToken);
        var completed = await orders.CountAsync(order => order.Status == "COMPLETED", cancellationToken);
        var cancelled = await orders.CountAsync(order => order.Status == "CANCELLED", cancellationToken);
        var reservations = await Db.Set<Reservation>().CountAsync(reservation =>
            reservation.StartsAtUtc >= startUtc && reservation.StartsAtUtc < endUtc, cancellationToken);
        var accounts = await Db.Set<DoraFood.Models.User>().CountAsync(user => user.IsActive, cancellationToken);
        var contacts = await Db.Set<ContactMessage>().CountAsync(message => message.Status == "NEW", cancellationToken);
        var hiddenComments = await Db.Set<PostComment>().CountAsync(item => item.Status == "REJECTED", cancellationToken);
        var hiddenReviews = await Db.Set<DishReview>().CountAsync(item => item.Status == "REJECTED", cancellationToken);
        var recent = await orders.OrderByDescending(order => order.OpenedAtUtc).ThenByDescending(order => order.Id)
            .Take(8).Select(order => new OverviewOrderRow(order.Code, order.OrderType, order.Status,
                order.GuestName, order.OpenedAtUtc)).ToListAsync(cancellationToken);

        return View(new OverviewModel(start, end, totalOrders, completed, cancelled, reservations,
            accounts, contacts, hiddenComments, hiddenReviews, days, recent));
    }
}

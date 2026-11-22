using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DoraFood.Areas.Admin.Filters;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[RequirePermission("reports.view")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RevenueController : Controller
{
    private readonly DoraFoodContext _context;

    public RevenueController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    [RequirePermission("reports.view")]
    public async Task<IActionResult> Index(
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest("Ngày thống kê không hợp lệ.");
        }

        // Mặc định: từ đầu tháng đến hôm nay theo giờ Việt Nam.
        var today = DateTime.UtcNow.AddHours(7).Date;

        var firstDayOfMonth = new DateTime(
            today.Year,
            today.Month,
            1);

        var startDate = DateTime.SpecifyKind(
            fromDate?.Date ?? firstDayOfMonth,
            DateTimeKind.Unspecified);

        var endDate = DateTime.SpecifyKind(
            toDate?.Date ?? today,
            DateTimeKind.Unspecified);

        if (endDate < startDate)
        {
            return BadRequest(
                "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");
        }

        var dayCount = (endDate - startDate).Days + 1;

        if (dayCount > 366)
        {
            return BadRequest(
                "Mỗi lần thống kê tối đa 366 ngày.");
        }

        // Chuyển khoảng ngày Việt Nam thành khoảng UTC để lọc SQL.
        DateTime startUtc;
        DateTime endUtc;

        try
        {
            startUtc = DateTime.SpecifyKind(
                startDate.AddHours(-7),
                DateTimeKind.Utc);

            // Lấy đến trước 00:00 ngày kế tiếp.
            endUtc = DateTime.SpecifyKind(
                endDate.AddDays(1).AddHours(-7),
                DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest("Ngày thống kê vượt giới hạn cho phép.");
        }

        var (collectedByDay, refundedByDay) =
    await LoadSettlementByDayAsync(startUtc, endUtc, cancellationToken);

        // 3. ĐƠN HOÀN TẤT TRONG KỲ
        var completedOrders = _context.Set<Order>()
            .AsNoTracking()
            .Where(order =>
                order.Status == "COMPLETED"
                && order.ClosedAtUtc.HasValue
                && order.ClosedAtUtc.Value >= startUtc
                && order.ClosedAtUtc.Value < endUtc);

        var completedByDay = await completedOrders
            .GroupBy(order =>
                order.ClosedAtUtc!.Value.AddHours(7).Date)
            .Select(group => new
            {
                Date = group.Key,
                Count = group.LongCount()
            })
            .ToDictionaryAsync(
                item => item.Date,
                item => item.Count,
                cancellationToken);

        // 4. TIỀN GIẢM CỦA CÁC ĐƠN HOÀN TẤT TRONG KỲ
        // Lấy khoản giảm đã lưu, không tính lại voucher hiện tại.
        var discountsByDay = await (
            from discount in _context.Set<OrderDiscount>().AsNoTracking()
            join order in completedOrders
                on discount.OrderId equals order.Id
            group discount by
                order.ClosedAtUtc!.Value.AddHours(7).Date
                into daily
            select new
            {
                Date = daily.Key,
                Amount = daily.Sum(discount => discount.Amount)
            })
            .ToDictionaryAsync(
                item => item.Date,
                item => item.Amount,
                cancellationToken);

        // Ghép thành đủ các ngày, kể cả ngày không phát sinh.
        var days = Enumerable.Range(0, dayCount)
            .Select(offset =>
            {
                var date = startDate.AddDays(offset);

                collectedByDay.TryGetValue(date, out var collected);
                refundedByDay.TryGetValue(date, out var refunded);
                completedByDay.TryGetValue(date, out var completed);
                discountsByDay.TryGetValue(date, out var discount);

                return new RevenueDailyRow(
                    date,
                    collected,
                    refunded,
                    completed,
                    discount);
            })
            .ToList();

        ViewData["Title"] = "Thống kê doanh thu";

        return View(new RevenueDashboardViewModel
        {
            FromDate = startDate,
            ToDate = endDate,
            Days = days
        });
    }
    [HttpGet]
    [RequirePermission("reports.view")]
    public async Task<IActionResult> Forecast(CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        const int historyDays = 56;
        const int forecastDays = 7;
        const int minimumSamples = 3;

        var today = DateTime.SpecifyKind(
            DateTime.UtcNow.AddHours(7).Date,
            DateTimeKind.Unspecified);

        var historyStart = today.AddDays(-historyDays);

        // Lấy 56 ngày đã kết thúc theo giờ Việt Nam.
        var startUtc = DateTime.SpecifyKind(
            historyStart.AddHours(-7), DateTimeKind.Utc);

        var endUtc = DateTime.SpecifyKind(
            today.AddHours(-7), DateTimeKind.Utc);

        var (collectedByDay, refundedByDay) =
            await LoadSettlementByDayAsync(startUtc, endUtc, cancellationToken);

        // Bắt đầu từ ngày đầu tiên có giao dịch trong khoảng lịch sử.
        var firstObservedDay = collectedByDay.Keys
            .Concat(refundedByDay.Keys)
            .Select(date => (DateTime?)date)
            .Min();

        var history = Enumerable.Range(0, historyDays)
            .Select(offset => historyStart.AddDays(offset))
            .Where(date => firstObservedDay.HasValue
                && date >= firstObservedDay.Value)
            .GroupBy(date => date.DayOfWeek)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Count = group.Count(),
                    Average = group.Average(date =>
                        collectedByDay.GetValueOrDefault(date)
                        - refundedByDay.GetValueOrDefault(date))
                });

        var forecast = new List<RevenueForecastRow>(forecastDays);

        for (var offset = 1; offset <= forecastDays; offset++)
        {
            var date = today.AddDays(offset);
            history.TryGetValue(date.DayOfWeek, out var sample);

            var sampleCount = sample?.Count ?? 0;
            decimal? prediction = sampleCount >= minimumSamples
                ? Math.Round(sample!.Average, 0, MidpointRounding.AwayFromZero)
                : null;

            forecast.Add(new RevenueForecastRow(date, prediction, sampleCount));
        }

        decimal? total = forecast.All(day => day.PredictedNetRevenue.HasValue)
            ? forecast.Sum(day => day.PredictedNetRevenue.GetValueOrDefault())
            : null;

        return View(new RevenueForecastViewModel(
            firstObservedDay,
            today.AddDays(-1),
            forecast,
            total));
    }

    private async Task<(
        Dictionary<DateTime, decimal> Collected,
        Dictionary<DateTime, decimal> Refunded)> LoadSettlementByDayAsync(
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken)
    {
        var collected = await _context.Set<Payment>()
            .AsNoTracking()
            .Where(payment =>
                payment.Status == "SUCCEEDED"
                && payment.Purpose == "SETTLEMENT"
                && payment.PaidAtUtc.HasValue
                && payment.PaidAtUtc.Value >= startUtc
                && payment.PaidAtUtc.Value < endUtc)
            .GroupBy(payment => payment.PaidAtUtc!.Value.AddHours(7).Date)
            .Select(group => new
            {
                Date = group.Key,
                Amount = group.Sum(payment => payment.Amount)
            })
            .ToDictionaryAsync(
                item => item.Date, item => item.Amount, cancellationToken);

        var refunded = await (
            from refund in _context.Set<Refund>().AsNoTracking()
            join payment in _context.Set<Payment>().AsNoTracking()
                on refund.PaymentId equals payment.Id
            where refund.Status == "SUCCEEDED"
                && payment.Purpose == "SETTLEMENT"
                && refund.RefundedAtUtc.HasValue
                && refund.RefundedAtUtc.Value >= startUtc
                && refund.RefundedAtUtc.Value < endUtc
            group refund by refund.RefundedAtUtc!.Value.AddHours(7).Date
            into daily
            select new
            {
                Date = daily.Key,
                Amount = daily.Sum(refund => refund.Amount)
            })
            .ToDictionaryAsync(
                item => item.Date, item => item.Amount, cancellationToken);

        return (collected, refunded);
    }
    private async Task<bool> CanViewAsync(
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            || userId <= 0)
        {
            return false;
        }

        return await _context.Set<DoraFood.Models.User>()
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId
                    && user.IsActive
                    && user.Roles.Any(role =>
                        role.Code == "ADMIN"
                        || role.Code == "MANAGER"),
                cancellationToken);
    }
}
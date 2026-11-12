using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.ViewComponents;

public sealed class OrderCancellationViewComponent : ViewComponent
{
    private readonly DoraFoodContext _context;

    public OrderCancellationViewComponent(DoraFoodContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync(long id)
    {
        var user = HttpContext.User;

        if (id <= 0
            || user.Identity?.IsAuthenticated != true
            || !long.TryParse(
                user.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId))
        {
            return Content(string.Empty);
        }

        var ct = HttpContext.RequestAborted;

        var isActive = await _context.Set<DoraFood.Models.User>()
            .AsNoTracking()
            .AnyAsync(
                account => account.Id == userId && account.IsActive,
                ct);

        if (!isActive)
        {
            return Content(string.Empty);
        }

        var payments = _context.Set<Payment>().AsNoTracking();

        var pendingCash =
            from payment in payments
            join method in _context.Set<PaymentMethod>().AsNoTracking()
                on payment.MethodId equals method.Id
            where payment.Status == "PENDING"
                && payment.Purpose == "SETTLEMENT"
                && payment.Amount > 0m
                && method.Code == "CASH"
            select payment;

        // Chỉ đọc đơn giao hàng thuộc tài khoản hiện tại.
        var order = await _context.Set<Order>()
            .AsNoTracking()
            .Where(item =>
                item.Id == id
                && item.CustomerId == userId
                && item.OrderType == "DELIVERY")
            .Select(item => new
            {
                item.Id,
                item.Code,
                item.Status,
                item.ClosedAtUtc,

                HasCollected = payments.Any(payment =>
                    payment.OrderId == item.Id
                    && payment.Status == "SUCCEEDED"),

                PendingCount = payments.Count(payment =>
                    payment.OrderId == item.Id
                    && payment.Status == "PENDING"),

                HasPendingCash = pendingCash.Any(payment =>
                    payment.OrderId == item.Id)
            })
            .SingleOrDefaultAsync(ct);

        if (order is null)
        {
            return Content(string.Empty);
        }

        var requests = _context.Set<OrderCancellationRequest>()
            .AsNoTracking()
            .Where(request => request.OrderId == order.Id);

        var hasPending = await requests.AnyAsync(
            request => request.Status == "PENDING",
            ct);

        // Hiển thị tối đa 10 yêu cầu gần nhất của khách trên đơn này.
        var history = await requests
            .Where(request => request.RequestedBy == userId)
            .OrderByDescending(request => request.RequestedAtUtc)
            .ThenByDescending(request => request.Id)
            .Take(10)
            .Select(request => new MyOrderCancellationRow(
                request.Status,
                request.Reason,
                request.RequestedAtUtc,
                request.ReviewedAtUtc,
                request.ReviewNote))
            .ToListAsync(ct);

        var model = new MyOrderCancellationViewModel
        {
            OrderId = order.Id,
            Code = order.Code,
            HasPendingRequest = hasPending,

            CanRequest = (order.Status is "OPEN" or "IN_PROGRESS")
                && order.ClosedAtUtc is null
                && !order.HasCollected
                && order.PendingCount == 1
                && order.HasPendingCash
                && !hasPending,

            Requests = history
        };

        return View(model);
    }
}
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
using System.Security.Claims;
namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DeliveryOrdersController : Controller
{
    private readonly DoraFoodContext _context;

    public DeliveryOrdersController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 20;

        // Lấy các khoản thanh toán tiền mặt cho đơn hàng.
        var cashPayments =
            from payment in _context.Set<Payment>().AsNoTracking()
            join method in _context.Set<PaymentMethod>().AsNoTracking()
                on payment.MethodId equals method.Id
            where method.Code == "CASH"
                && payment.Purpose == "SETTLEMENT"
            select payment;

        // Chỉ lấy đơn giao hàng có khoản thanh toán tiền mặt.
        var deliveryOrders = _context.Set<Order>()
            .AsNoTracking()
            .Where(order => order.OrderType == "DELIVERY"
                && cashPayments.Any(payment => payment.OrderId == order.Id));

        var totalOrders = await deliveryOrders.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalOrders / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var orders = await deliveryOrders
            .OrderByDescending(order => order.OpenedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => new DeliveryOrderRow(
                order.Code,
                order.GuestName,
                order.GuestPhone,
                order.DeliveryAddress,
                order.OpenedAtUtc,
                order.Status,
                cashPayments
                    .Where(payment => payment.OrderId == order.Id
                        && payment.Status == "PENDING")
                    .Sum(payment => (decimal?)payment.Amount) ?? 0m,
                cashPayments
                    .Where(payment => payment.OrderId == order.Id
                        && payment.Status == "SUCCEEDED")
                    .Sum(payment => (decimal?)payment.Amount) ?? 0m))
            .ToListAsync(cancellationToken);

        return View(new DeliveryOrderListViewModel(
            orders, page, totalPages, totalOrders));
    }
    [HttpGet]
    public async Task<IActionResult> Details(
    string? code,
    int page = 1,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 40)
        {
            return NotFound();
        }

        code = code.Trim();

        var cashPayments =
            from payment in _context.Set<Payment>().AsNoTracking()
            join method in _context.Set<PaymentMethod>().AsNoTracking()
                on payment.MethodId equals method.Id
            where method.Code == "CASH"
                && payment.Purpose == "SETTLEMENT"
            select payment;

        var order = await _context.Set<Order>()
            .AsNoTracking()
            .Where(order => order.Code == code
                && order.OrderType == "DELIVERY"
                && cashPayments.Any(payment => payment.OrderId == order.Id))
            .Select(order => new
            {
                order.Id,
                order.Code,
                order.GuestName,
                order.GuestPhone,
                order.DeliveryAddress,
                order.Note,
                order.OpenedAtUtc,
                order.Status,
                PendingAmount = cashPayments
                    .Where(payment => payment.OrderId == order.Id
                        && payment.Status == "PENDING")
                    .Sum(payment => (decimal?)payment.Amount) ?? 0m,
                ReceivedAmount = cashPayments
                    .Where(payment => payment.OrderId == order.Id
                        && payment.Status == "SUCCEEDED")
                    .Sum(payment => (decimal?)payment.Amount) ?? 0m
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order == null)
        {
            return NotFound();
        }

        var items = await _context.Set<OrderItem>()
            .AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .OrderBy(item => item.Id)
            .Select(item => new DeliveryOrderItemRow(
                item.NameSnapshot,
                item.Quantity,
                item.UnitPrice,
                item.Status))
            .ToListAsync(cancellationToken);

        var model = new DeliveryOrderDetailsViewModel(
            order.Code,
            order.GuestName,
            order.GuestPhone,
            order.DeliveryAddress,
            order.Note,
            order.OpenedAtUtc,
            order.Status,
            order.PendingAmount,
            order.ReceivedAmount,
            items,
            Math.Max(1, page));

        return View(model);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(
    [FromForm] string? code,
    [FromForm] int page = 1,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 40)
        {
            return BadRequest("Mã đơn không hợp lệ.");
        }

        code = code.Trim();

        var pendingCashPayments =
            from payment in _context.Set<Payment>()
            join method in _context.Set<PaymentMethod>()
                on payment.MethodId equals method.Id
            where method.Code == "CASH"
                && payment.Purpose == "SETTLEMENT"
                && payment.Status == "PENDING"
            select payment;

        var updatedRows = await _context.Set<Order>()
            .Where(order => order.Code == code
                && order.OrderType == "DELIVERY"
                && order.Status == "OPEN"
                && pendingCashPayments.Any(payment => payment.OrderId == order.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    order => order.Status, "IN_PROGRESS"),
                cancellationToken);

        if (updatedRows == 1)
        {
            TempData["DeliverySuccess"] = $"Đã tiếp nhận đơn {code}.";
        }
        else
        {
            TempData["DeliveryError"] =
                "Đơn không còn đủ điều kiện tiếp nhận. Anh kiểm tra lại trạng thái đơn.";
        }

        return RedirectToAction(nameof(Details),
            new { area = "Admin", code, page = Math.Max(1, page) });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteCashDelivery(
    [FromForm] string? code,
    [FromForm] int page = 1,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 40)
        {
            return BadRequest("Mã đơn không hợp lệ.");
        }

        code = code.Trim();

        IActionResult BackWithError(string message)
        {
            TempData["DeliveryError"] = message;

            return RedirectToAction(nameof(Details),
                new { area = "Admin", code, page = Math.Max(1, page) });
        }

     

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!long.TryParse(userIdClaim, out var userId) || userId <= 0)
        {
            return Forbid();
        }

        var now = DateTime.UtcNow;

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        var orders = _context.Set<Order>()
            .Where(order => order.Code == code && order.OrderType == "DELIVERY");

        // Chỉ một yêu cầu có thể chuyển đơn đang xử lý sang hoàn tất.
        var updatedOrders = await orders
            .Where(order => order.Status == "IN_PROGRESS")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(order => order.Status, "COMPLETED")
                    .SetProperty(order => order.ClosedAtUtc, (DateTime?)now),
                cancellationToken);

        if (updatedOrders != 1)
        {
            return BackWithError(
                "Chỉ đơn đang xử lý mới được xác nhận giao và thu tiền.");
        }

        var settlements = await (
            from payment in _context.Set<Payment>().AsNoTracking()
            join order in orders on payment.OrderId equals order.Id
            join method in _context.Set<PaymentMethod>().AsNoTracking()
                on payment.MethodId equals method.Id
            where payment.Purpose == "SETTLEMENT"
                && (payment.Status == "PENDING" || payment.Status == "SUCCEEDED")
            select new
            {
                payment.Id,
                payment.Amount,
                payment.Status,
                MethodCode = method.Code
            })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (settlements.Count != 1
            || settlements[0].MethodCode != "CASH"
            || settlements[0].Status != "PENDING")
        {
            return BackWithError(
                "Đơn không có đúng một khoản tiền mặt đang chờ thu. Anh kiểm tra lại.");
        }

        var paymentToCollect = settlements[0];

        var updatedPayments = await _context.Set<Payment>()
            .Where(payment => payment.Id == paymentToCollect.Id
                && payment.Status == "PENDING"
                && payment.Amount == paymentToCollect.Amount)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(payment => payment.Status, "SUCCEEDED")
                    .SetProperty(payment => payment.PaidAtUtc, (DateTime?)now)
                    .SetProperty(payment => payment.ReceivedBy, (long?)userId),
                cancellationToken);

        if (updatedPayments != 1)
        {
            return BackWithError(
                "Khoản thu vừa thay đổi. Anh tải lại chi tiết đơn để kiểm tra.");
        }

        await transaction.CommitAsync(cancellationToken);

        TempData["DeliverySuccess"] =
            $"Đã xác nhận giao hàng và thu tiền cho đơn {code}.";

        return RedirectToAction(nameof(Details),
            new { area = "Admin", code, page = Math.Max(1, page) });
    }
}
using System;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
        if (!ModelState.IsValid)
            return BadRequest("Số trang không hợp lệ.");

        const int pageSize = 20;

        // Lấy các khoản thanh toán tiền mặt cho đơn hàng.
        var cashPayments = CashSettlements();

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
        if (!ModelState.IsValid || !ValidCode(code))
        {
            return NotFound();
        }

        code = code!.Trim();

        var cashPayments = CashSettlements();

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
    // OPEN -> IN_PROGRESS: nhà hàng tiếp nhận và chuẩn bị món.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Accept(
        [FromForm] string? code,
        [FromForm] int page = 1,
        CancellationToken cancellationToken = default) =>
        ChangeDeliveryAsync(code, page, "OPEN", "IN_PROGRESS",
            "Đã tiếp nhận đơn. Nhà hàng bắt đầu chuẩn bị món.", cancellationToken);

    // IN_PROGRESS -> SHIPPING: bàn giao món cho người giao hàng.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> StartShipping(
        [FromForm] string? code,
        [FromForm] int page = 1,
        CancellationToken cancellationToken = default) =>
        ChangeDeliveryAsync(code, page, "IN_PROGRESS", "SHIPPING",
            "Đơn đang được giao đến khách.", cancellationToken);

    // SHIPPING -> COMPLETED: chỉ bấm khi đã giao hàng và thu đủ tiền mặt.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CompleteCashDelivery(
        [FromForm] string? code,
        [FromForm] int page = 1,
        CancellationToken cancellationToken = default) =>
        ChangeDeliveryAsync(code, page, "SHIPPING", "COMPLETED",
            "Đã xác nhận giao hàng và thu tiền mặt thành công.", cancellationToken);

    // Dùng chung kiểm tra, giao dịch và thông báo cho ba thao tác trên.
    private async Task<IActionResult> ChangeDeliveryAsync(
        string? code, int page, string fromStatus, string toStatus,
        string successMessage, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !ValidCode(code))
            return BadRequest("Mã đơn hoặc số trang không hợp lệ.");

        code = code!.Trim();
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || userId <= 0)
            return Forbid();

        var actorIsValid = await _context.Set<DoraFood.Models.User>().AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive
                && user.Roles.Any(role => role.Code == "ADMIN" || role.Code == "MANAGER"),
                cancellationToken);
        if (!actorIsValid) return Forbid();

        string? error;
        try
        {
            // Phù hợp cả khi kết nối SQL đã bật EnableRetryOnFailure.
            error = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // Giữ ổn định các khoản thanh toán đã đọc đến khi commit.
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

                var now = DateTime.UtcNow;
                var completing = toStatus == "COMPLETED";
                var orders = _context.Set<Order>()
                    .Where(order => order.Code == code && order.OrderType == "DELIVERY");

                // Cập nhật có điều kiện: một đơn chỉ đi tiếp từ đúng trạng thái trước đó.
                var updatedOrders = await orders
                    .Where(order => order.Status == fromStatus && order.ClosedAtUtc == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(order => order.Status, toStatus)
                        .SetProperty(order => order.ClosedAtUtc,
                            completing ? (DateTime?)now : null), cancellationToken);

                if (updatedOrders != 1)
                    return "Đơn không còn ở bước phù hợp. Hãy tải lại chi tiết đơn để kiểm tra.";

                // Cả ba bước đều yêu cầu đúng một khoản thanh toán đang có hiệu lực:
                // tiền mặt, mục đích thanh toán đơn và đang chờ thu.
                // Bản ghi FAILED/CANCELLED của các lần thử cũ không được tính.
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
                        payment.OrderId,
                        payment.MethodId,
                        payment.Amount,
                        payment.Status,
                        MethodCode = method.Code
                    })
                    .Take(2)
                    .ToListAsync(cancellationToken);

                if (settlements.Count != 1
                    || settlements[0].MethodCode != "CASH"
                    || settlements[0].Status != "PENDING"
                    || settlements[0].Amount <= 0m)
                    return "Đơn phải có đúng một khoản tiền mặt đang chờ thu hợp lệ. Hãy kiểm tra thanh toán.";

                if (completing)
                {
                    var paymentToCollect = settlements[0];
                    var updatedPayments = await _context.Set<Payment>()
                        .Where(payment => payment.Id == paymentToCollect.Id
                            && payment.OrderId == paymentToCollect.OrderId
                            && payment.MethodId == paymentToCollect.MethodId
                            && payment.Purpose == "SETTLEMENT"
                            && payment.Status == "PENDING"
                            && payment.Amount == paymentToCollect.Amount)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(payment => payment.Status, "SUCCEEDED")
                            .SetProperty(payment => payment.PaidAtUtc, (DateTime?)now)
                            .SetProperty(payment => payment.ReceivedBy, (long?)userId),
                            cancellationToken);

                    if (updatedPayments != 1)
                        return "Khoản thu vừa thay đổi. Hãy tải lại chi tiết đơn để kiểm tra.";
                }

                await transaction.CommitAsync(cancellationToken);
                return (string?)null;
                // Nếu return lỗi ở phía trên, DisposeAsync hoàn tác giao dịch chưa commit.
            });
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            error = "Đơn đang được xử lý đồng thời. Hãy tải lại chi tiết rồi thử lại.";
        }

        TempData[error is null ? "DeliverySuccess" : "DeliveryError"] =
            error ?? $"{successMessage} Mã đơn: {code}.";

        return RedirectToAction(nameof(Details),
            new { area = "Admin", code, page = Math.Max(1, page) });
    }

    private IQueryable<Payment> CashSettlements() =>
        from payment in _context.Set<Payment>().AsNoTracking()
        join method in _context.Set<PaymentMethod>().AsNoTracking()
            on payment.MethodId equals method.Id
        where method.Code == "CASH" && payment.Purpose == "SETTLEMENT"
        select payment;

    private static bool ValidCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 40;
}

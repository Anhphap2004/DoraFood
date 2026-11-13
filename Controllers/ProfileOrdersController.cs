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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

[Authorize]
[Route("tai-khoan/don-hang")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileOrdersController : Controller
{
    private const int PageSize = 10;

    private readonly DoraFoodContext _context;

    public ProfileOrdersController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet(Name = "DoraFood.MyOrders")]
    public async Task<IActionResult> Index(
     CancellationToken cancellationToken,
     [FromQuery] int pageNumber = 1)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Số trang không hợp lệ.");
        }

        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        var orders = DeliveryOrdersOf(userId);
        var payments = SettlementPayments();

        var items = _context.Set<OrderItem>()
            .AsNoTracking();

        var discounts = _context.Set<OrderDiscount>()
            .AsNoTracking();

        var totalOrders = await orders.CountAsync(cancellationToken);

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalOrders / (double)PageSize));

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var rows = await orders
            .OrderByDescending(order => order.OpenedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(order => new MyOrderRow(
                order.Id,
                order.Code,
                order.OpenedAtUtc,
                order.Status,

                // Ưu tiên số tiền đã ghi nhận khi tạo thanh toán.
                payments
                    .Where(payment => payment.OrderId == order.Id)
                    .OrderByDescending(payment => payment.Status == "SUCCEEDED")
                    .ThenByDescending(payment => payment.Id)
                    .Select(payment => (decimal?)payment.Amount)
                    .FirstOrDefault()

                // Nếu chưa có thanh toán: tiền món trừ khoản giảm đã lưu.
                ?? (
                    (items
                        .Where(item => item.OrderId == order.Id)
                        .Sum(item =>
                            (decimal?)(item.Quantity * item.UnitPrice))
                        ?? 0m)

                    - (discounts
                        .Where(discount => discount.OrderId == order.Id)
                        .Sum(discount => (decimal?)discount.Amount)
                        ?? 0m)
                )))
            .ToListAsync(cancellationToken);

        return View(new MyOrdersViewModel(
            rows,
            pageNumber,
            totalPages,
            totalOrders));
    }

    [HttpGet("{id:long}", Name = "DoraFood.MyOrderDetails")]
    public async Task<IActionResult> Details(
    [FromRoute] long id,
    CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return NotFound();
        }

        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        // Mọi truy vấn đều giới hạn theo đơn của tài khoản hiện tại.
        var orders = DeliveryOrdersOf(userId);
        var payments = SettlementPayments();

        var order = await orders
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.Code,
                item.OpenedAtUtc,
                item.Status,
                item.GuestName,
                item.GuestPhone,
                item.DeliveryAddress,
                item.Note,

                PaymentAmount = payments
                    .Where(payment => payment.OrderId == item.Id)
                    .OrderByDescending(payment => payment.Status == "SUCCEEDED")
                    .ThenByDescending(payment => payment.Id)
                    .Select(payment => (decimal?)payment.Amount)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        // Đọc tên và giá món đã lưu trong đơn.
        var items = await (
            from item in _context.Set<OrderItem>().AsNoTracking()
            join ownedOrder in orders
                on item.OrderId equals ownedOrder.Id
            where ownedOrder.Id == id
            orderby item.Id
            select new MyOrderItemRow(
                item.NameSnapshot,
                item.Quantity,
                item.UnitPrice,
                item.Status))
            .ToListAsync(cancellationToken);

        // Đọc khoản giảm thực tế đã áp dụng.
        // Không tính lại theo cấu hình voucher hiện tại.
        var discounts = await (
            from discount in _context.Set<OrderDiscount>().AsNoTracking()
            join ownedOrder in orders
                on discount.OrderId equals ownedOrder.Id
            where ownedOrder.Id == id
            orderby discount.Id
            select new MyOrderDiscountRow(
                discount.Description,
                discount.Amount))
            .ToListAsync(cancellationToken);

        var subtotal = items.Sum(item => item.LineTotal);
        var discountAmount = discounts.Sum(discount => discount.Amount);

        var totalAmount = order.PaymentAmount
            ?? (subtotal - discountAmount);

        // Giữ lịch sử xử lý đơn đang có.
        var history = await (
            from entry in _context.Set<OrderStatusHistory>().AsNoTracking()
            join ownedOrder in orders
                on entry.OrderId equals ownedOrder.Id
            where ownedOrder.Id == id
                && (entry.FromStatus != null || entry.ToStatus != "OPEN")
            orderby entry.ChangedAtUtc, entry.Id
            select new MyOrderHistoryRow(
                entry.ToStatus,
                entry.ChangedAtUtc,
                false))
            .ToListAsync(cancellationToken);

        history.Insert(
            0,
            new MyOrderHistoryRow(
                "OPEN",
                order.OpenedAtUtc,
                true));

        var summary = new MyOrderRow(
            order.Id,
            order.Code,
            order.OpenedAtUtc,
            order.Status,
            totalAmount);

        var model = new MyOrderDetailsViewModel(
            summary,
            order.GuestName,
            order.GuestPhone,
            order.DeliveryAddress,
            order.Note,
            items)
        {
            Subtotal = subtotal,
            DiscountAmount = discountAmount,
            Discounts = discounts,
            History = history
        };

        return View(model);
    }
    [HttpPost("{id:long}/huy", Name = "DoraFood.CancelMyOrder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
    [FromRoute] long id,
    [FromForm] OrderCancellationRequestInput input,
    CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        input.Code = input.Code?.Trim() ?? "";
        input.Reason = input.Reason?.Trim() ?? "";

        IActionResult BackWithError(string message)
        {
            TempData["OrderError"] = message;

            // Giữ lý do để khách sửa lại khi có lỗi.
            TempData["OrderCancelReason"] = input.Reason.Length > 500
                ? input.Reason[..500]
                : input.Reason;

            return RedirectToRoute(
                "DoraFood.MyOrderDetails",
                new { id });
        }

        if (!ModelState.IsValid
            || input.Code.Length is 0 or > 40
            || string.IsNullOrWhiteSpace(input.Reason)
            || input.Reason.Length > 500)
        {
            return BackWithError(
                "Mã đơn không hợp lệ hoặc lý do hủy chưa được nhập đúng, tối đa 500 ký tự.");
        }

        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
            SET XACT_ABORT ON;

            BEGIN TRY
                BEGIN TRANSACTION;

                -- Khóa dòng đơn trong lúc kiểm tra và gửi yêu cầu.
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
                    WHERE Id = {id}
                      AND Code = {input.Code}
                      AND CustomerId = {userId}
                      AND OrderType = 'DELIVERY'
                      AND Status IN ('OPEN', 'IN_PROGRESS')
                      AND ClosedAtUtc IS NULL
                )
                BEGIN
                    THROW 51010,
                        N'Đơn không còn đủ điều kiện gửi yêu cầu hủy.',
                        1;
                END;

                -- Không nhận thêm yêu cầu nếu đang có yêu cầu chờ.
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.OrderCancellationRequests
                        WITH (UPDLOCK, HOLDLOCK)
                    WHERE OrderId = {id}
                      AND Status = 'PENDING'
                )
                BEGIN
                    THROW 51011,
                        N'Đơn đã có yêu cầu hủy đang chờ quản lý xử lý.',
                        1;
                END;

                -- Chặn nếu đã thu tiền, kể cả khoản cọc.
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
                    WHERE OrderId = {id}
                      AND Status = 'SUCCEEDED'
                )
                BEGIN
                    THROW 51012,
                        N'Đơn đã ghi nhận thu tiền. Anh hãy liên hệ nhà hàng để được hỗ trợ.',
                        1;
                END;

                -- COD hiện tại phải có đúng một khoản tiền mặt chờ thu.
                IF
                (
                    SELECT COUNT_BIG(*)
                    FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
                    WHERE OrderId = {id}
                      AND Status = 'PENDING'
                ) <> 1
                OR NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.Payments AS p WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.PaymentMethods AS m WITH (HOLDLOCK)
                        ON m.Id = p.MethodId
                    WHERE p.OrderId = {id}
                      AND p.Status = 'PENDING'
                      AND p.Purpose = 'SETTLEMENT'
                      AND p.Amount > 0
                      AND m.Code = 'CASH'
                )
                BEGIN
                    THROW 51013,
                        N'Thông tin thanh toán của đơn cần được nhà hàng kiểm tra.',
                        1;
                END;

                -- SQL tự đặt PENDING, thời điểm gửi và Version.
                INSERT INTO dbo.OrderCancellationRequests
                    (OrderId, RequestedBy, Reason)
                VALUES
                    ({id}, {userId}, {input.Reason});

                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0
                    ROLLBACK TRANSACTION;

                THROW;
            END CATCH;
            """, cancellationToken);

            TempData.Remove("OrderCancelReason");

            TempData["OrderSuccess"] =
                "Đã gửi yêu cầu hủy. Đơn chưa bị hủy; nhà hàng sẽ xem xét và phản hồi.";
        }
        catch (SqlException exception)
            when (exception.Number is 51010 or 51011 or 51012 or 51013)
        {
            return BackWithError(exception.Message);
        }
        catch (SqlException exception)
            when (exception.Number is 2601 or 2627)
        {
            return BackWithError(
                "Đơn đã có yêu cầu hủy đang chờ xử lý. Anh hãy tải lại trang.");
        }
        catch (SqlException exception)
            when (exception.Number == 1205)
        {
            return BackWithError(
                "Đơn đang được cập nhật. Anh hãy tải lại trang rồi thử lại.");
        }

        return RedirectToRoute(
            "DoraFood.MyOrderDetails",
            new { id });
    }
    private IQueryable<Order> DeliveryOrdersOf(long userId)
    {
        return _context.Set<Order>()
            .AsNoTracking()
            .Where(order =>
                order.CustomerId == userId
                && order.OrderType == "DELIVERY");
    }

    private IQueryable<Payment> SettlementPayments()
    {
        return _context.Set<Payment>()
            .AsNoTracking()
            .Where(payment => payment.Purpose == "SETTLEMENT");
    }

    private async Task<long?> GetActiveUserIdAsync(
        CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(claim, out var userId) || userId <= 0)
        {
            return null;
        }

        var isActive = await _context.Set<User>().AnyAsync(
            user => user.Id == userId && user.IsActive,
            cancellationToken);

        return isActive ? userId : null;
    }
}
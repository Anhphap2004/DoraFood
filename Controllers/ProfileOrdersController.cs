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
        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        var orders = DeliveryOrdersOf(userId);
        var payments = SettlementPayments();
        var items = _context.Set<OrderItem>().AsNoTracking();

        var totalOrders = await orders.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalOrders / (double)PageSize));
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
                payments
                    .Where(payment => payment.OrderId == order.Id)
                    .OrderByDescending(payment => payment.Status == "SUCCEEDED")
                    .ThenByDescending(payment => payment.Id)
                    .Select(payment => (decimal?)payment.Amount)
                    .FirstOrDefault()
                ?? items
                    .Where(item => item.OrderId == order.Id)
                    .Sum(item => (decimal?)(item.Quantity * item.UnitPrice))
                ?? 0m))
            .ToListAsync(cancellationToken);

        return View(new MyOrdersViewModel(
            rows, pageNumber, totalPages, totalOrders));
    }

    [HttpGet("{id:long}", Name = "DoraFood.MyOrderDetails")]
    public async Task<IActionResult> Details(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

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

        var items = await (
            from item in _context.Set<OrderItem>().AsNoTracking()
            join ownedOrder in orders on item.OrderId equals ownedOrder.Id
            where ownedOrder.Id == id
            orderby item.Id
            select new MyOrderItemRow(
                item.NameSnapshot,
                item.Quantity,
                item.UnitPrice,
                item.Status))
            .ToListAsync(cancellationToken);

        var summary = new MyOrderRow(
            order.Id,
            order.Code,
            order.OpenedAtUtc,
            order.Status,
            order.PaymentAmount ?? items.Sum(item => item.LineTotal));

        return View(new MyOrderDetailsViewModel(
            summary,
            order.GuestName,
            order.GuestPhone,
            order.DeliveryAddress,
            order.Note,
            items));
    }
    [HttpPost("{id:long}/huy", Name = "DoraFood.CancelMyOrder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
 [FromRoute] long id,
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

        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
            SET XACT_ABORT ON;

            BEGIN TRY
                BEGIN TRANSACTION;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
                    WHERE Id = {id}
                      AND CustomerId = {userId}
                      AND OrderType = 'DELIVERY'
                      AND Status = 'OPEN'
                )
                BEGIN
                    THROW 51010, 'Order cannot be cancelled.', 1;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
                    WHERE OrderId = {id}
                      AND Status = 'SUCCEEDED'
                )
                OR EXISTS
                (
                    SELECT 1
                    FROM dbo.OrderItems WITH (UPDLOCK, HOLDLOCK)
                    WHERE OrderId = {id}
                      AND Status IN ('PREPARING', 'READY', 'SERVED')
                )
                BEGIN
                    THROW 51010, 'Order cannot be cancelled.', 1;
                END;

                DECLARE @CancelledAtUtc datetime2(3) = SYSUTCDATETIME();

                UPDATE dbo.Orders
                SET Status = 'CANCELLED',
                    ClosedAtUtc = CASE
                        WHEN OpenedAtUtc > @CancelledAtUtc THEN OpenedAtUtc
                        ELSE @CancelledAtUtc
                    END
                WHERE Id = {id};

                UPDATE dbo.OrderItems
                SET Status = 'CANCELLED'
                WHERE OrderId = {id} AND Status = 'NEW';

                UPDATE dbo.Payments
                SET Status = 'CANCELLED'
                WHERE OrderId = {id} AND Status = 'PENDING';

                INSERT INTO dbo.OrderStatusHistory
                    (OrderId, FromStatus, ToStatus, ChangedBy, Reason, ChangedAtUtc)
                VALUES
                    ({id}, 'OPEN', 'CANCELLED', {userId},
                     N'Khách hàng tự hủy đơn.', @CancelledAtUtc);

                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0
                    ROLLBACK TRANSACTION;

                THROW;
            END CATCH;
            """, cancellationToken);

            TempData["OrderSuccess"] = "Đã hủy đơn hàng.";
        }
        catch (SqlException exception) when (exception.Number == 51010)
        {
            TempData["OrderError"] =
                "Đơn này không còn hủy được trên website. "
                + "Nếu vẫn cần hủy, anh hãy liên hệ với quán.";
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            TempData["OrderError"] =
                "Đơn đang được cập nhật. Anh hãy kiểm tra trạng thái rồi thử lại.";
        }

        return RedirectToRoute("DoraFood.MyOrderDetails", new { id });
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
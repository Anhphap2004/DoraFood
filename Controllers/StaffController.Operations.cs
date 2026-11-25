using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Controllers;

public sealed partial class StaffController
{
    private long _actorId;
    private const string TableLockSql = """
        DECLARE @LockResult int;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = N'DoraFood.Reservations',
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 10000;
        IF @LockResult < 0
            THROW 56030, N'Không thể khóa dữ liệu đặt bàn.', 1;
        """;

    [HttpPost("ban/{tableId:long}/mo", Name = "DoraFoodStaffWalkIn")]
    public async Task<IActionResult> WalkIn(long tableId, int partySize, Guid operationId,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || partySize <= 0 || operationId == Guid.Empty)
            return BadRequest("Thông tin mở bàn không hợp lệ.");

        var code = $"WL-{operationId:N}";
        return await WriteAtTableAsync(async () =>
        {
            var previous = await OpenTableOrders().Where(order => order.Code == code
                && order.CreatedBy == _actorId && order.OrderTables.Any(link => link.TableId == tableId))
                .Select(order => (long?)order.Id).SingleOrDefaultAsync(cancellationToken);

            if (previous.HasValue)
                return RedirectToRoute("DoraFoodStaffOrder", new { id = previous.Value });

            var now = DateTime.UtcNow;
            var table = await _db.Set<DiningTable>().AsNoTracking()
                .Where(item => item.Id == tableId && item.IsActive && item.Area.IsActive)
                .Select(item => new
                {
                    item.Code,
                    item.Capacity,
                    Reserved = item.Reservations.Any(reservation => reservation.Status == "SEATED"
                        || (reservation.Status == "CONFIRMED"
                            && reservation.StartsAtUtc <= now && reservation.EndsAtUtc > now)),
                    Occupied = _db.Set<OrderTable>().Any(link =>
                        link.TableId == item.Id && link.ReleasedAtUtc == null)
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (table is null || table.Reserved || table.Occupied || partySize > table.Capacity)
                return StaffError("Bàn đang được giữ, đã sử dụng hoặc không đủ chỗ.");

            var order = new Order
            {
                Code = code,
                OrderType = "DINE_IN",
                Status = "OPEN",
                CreatedBy = _actorId,
                OpenedAtUtc = now
            };
            order.OrderTables.Add(new OrderTable { TableId = tableId, AssignedAtUtc = now });
            _db.Set<Order>().Add(order);
            await _db.SaveChangesAsync(cancellationToken);

            Audit(order.Id, "staff.order.walk-in", $"Mở bàn {table.Code} cho {partySize} khách.");
            return RedirectToRoute("DoraFoodStaffOrder", new { id = order.Id });
        }, cancellationToken);
    }

    [HttpPost("don/{id:long}/mon/{lineId:long}/huy", Name = "DoraFoodStaffCancelDish")]
    public Task<IActionResult> CancelDish(
    [FromRoute] long id, [FromRoute] long lineId,
    CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id <= 0 || lineId <= 0)
            return Task.FromResult(StaffError("Thông tin món cần hủy không hợp lệ.", id));

        return WriteAtTableAsync(async () =>
        {
            var lines = _db.Set<OrderItem>().Where(item => item.OrderId == id && item.Id == lineId);
            var line = await lines
                .Where(item => OpenTableOrders().Any(order => order.Id == item.OrderId))
                .Select(item => new { item.NameSnapshot, item.Status })
                .SingleOrDefaultAsync(cancellationToken);

            if (line is null)
                return StaffError("Đơn tại bàn đã đóng hoặc món không tồn tại.");
            if (line.Status == "CANCELLED")
                return RedirectToRoute("DoraFoodStaffOrder", new { id });
            if (await _db.Set<Invoice>().AnyAsync(invoice => invoice.OrderId == id, cancellationToken)
                || await _db.Set<Payment>().AnyAsync(payment => payment.OrderId == id
                    && payment.Purpose == "SETTLEMENT"
                    && (payment.Status == "PENDING" || payment.Status == "SUCCEEDED"), cancellationToken))
                return StaffError("Đơn đang thanh toán hoặc đã xuất hóa đơn.", id);

            var affectedRows = await lines.Where(item => item.Status != "CANCELLED")
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, "CANCELLED"),
                    cancellationToken);

            if (affectedRows != 1 || !await lines.AnyAsync(item => item.Status == "CANCELLED", cancellationToken))
                throw new DbUpdateException($"Không xác nhận được trạng thái hủy của dòng món {lineId}.");

            Audit(id, "staff.order.cancel-item", $"Hủy {line.NameSnapshot} (dòng {lineId}).");
            TempData["StaffSuccess"] = $"Đã hủy {line.NameSnapshot}.";
            return RedirectToRoute("DoraFoodStaffOrder", new { id });
        }, cancellationToken, id);
    }

    [HttpPost("don/{id:long}/dong-ban-trong", Name = "DoraFoodStaffCloseEmpty")]
    public Task<IActionResult> CloseEmpty(long id, CancellationToken cancellationToken)
    {
        return WriteAtTableAsync(async () =>
        {
            var order = await OpenTableOrders().AsTracking()
                .Include(item => item.OrderTables.Where(link => link.ReleasedAtUtc == null))
                .Include(item => item.Reservation)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (order is null)
                return StaffError("Bàn đã được giải phóng hoặc đơn không còn mở.");

            if (await _db.Set<OrderItem>().AnyAsync(item => item.OrderId == id
                    && item.Status != "CANCELLED", cancellationToken)
                || !await CanReleaseEmptyOrderAsync(order, cancellationToken))
                return StaffError("Chưa thể giải phóng bàn vì còn món, dịch vụ tính phí hoặc giao dịch cần xử lý.", id);

            var now = DateTime.UtcNow;
            var fromStatus = order.Status;
            order.Status = "CANCELLED";
            order.ClosedAtUtc = now;
            _db.Entry(order).Property(item => item.Status).IsModified = true;
            _db.Entry(order).Property(item => item.ClosedAtUtc).IsModified = true;
            foreach (var link in order.OrderTables)
            {
                link.ReleasedAtUtc = now;
                _db.Entry(link).Property(item => item.ReleasedAtUtc).IsModified = true;
            }

            if (order.Reservation is { } reservation)
            {
                reservation.Status = "COMPLETED";
                _db.Entry(reservation).Property(item => item.Status).IsModified = true;
                _db.Set<AuditLog>().Add(new AuditLog
                {
                    ActorId = _actorId,
                    EntityName = "Reservation",
                    EntityKey = reservation.Id.ToString(CultureInfo.InvariantCulture),
                    Action = "BOOKING_COMPLETED",
                    Summary = "Khách đã rời bàn. Giải phóng bàn sau khi hủy hết món.",
                    CreatedAtUtc = now
                });
            }

            _db.Set<OrderStatusHistory>().Add(new OrderStatusHistory
            {
                OrderId = id,
                FromStatus = fromStatus,
                ToStatus = "CANCELLED",
                ChangedBy = _actorId,
                ChangedAtUtc = now,
                Reason = "Giải phóng bàn không còn món."
            });
            Audit(id, "staff.order.close-empty", "Đã giải phóng bàn không còn món.");
            TempData["StaffSuccess"] = "Đã giải phóng bàn.";
            return RedirectToRoute("DoraFoodStaffDesk");
        }, cancellationToken, id);
    }

    private async Task<bool> CanReleaseEmptyOrderAsync(Order order, CancellationToken cancellationToken)
    {
        if (await _db.Set<Payment>().AnyAsync(payment => payment.OrderId == order.Id
                && (payment.Status == "PENDING" || payment.Status == "SUCCEEDED"), cancellationToken)
            || await _db.Set<Invoice>().AnyAsync(invoice => invoice.OrderId == order.Id, cancellationToken))
        {
            return false;
        }

        if (order.ReservationId is not long reservationId)
        {
            return true;
        }

        return !await _db.Set<ReservationService>().AnyAsync(service =>
                service.ReservationId == reservationId && service.UnitPrice > 0, cancellationToken)
            && !await _db.Set<Order>().AnyAsync(other =>
                other.ReservationId == reservationId && other.Id != order.Id
                && (other.Status == "OPEN" || other.Status == "IN_PROGRESS"
                    || other.OrderTables.Any(link => link.ReleasedAtUtc == null)), cancellationToken);
    }

    [HttpPost("don/{id:long}/phieu-kiem", Name = "DoraFoodStaffPrintCheck")]
    public Task<IActionResult> PrintCheck(long id, CancellationToken cancellationToken)
    {
        return WriteAtTableAsync(async () =>
        {
            var order = await OpenTableOrders()
                .Include(item => item.OrderTables.Where(link => link.ReleasedAtUtc == null))
                    .ThenInclude(link => link.Table)
                .Include(item => item.OrderItems.Where(line => line.Status != "CANCELLED"))
                    .ThenInclude(line => line.OrderItemAddOns)
                .AsSplitQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (order is null)
                return StaffError("Đơn đã đóng. Mở hóa đơn đã lập để in.");

            var rows = order.OrderItems.Where(item => item.Status != "CANCELLED").OrderBy(item => item.Id)
                .SelectMany(item => new[] { new CheckLine(item.NameSnapshot, item.Quantity, item.Note) }
                    .Concat(item.OrderItemAddOns.OrderBy(addon => addon.Id)
                        .Select(addon => new CheckLine("Thêm " + addon.NameSnapshot, addon.Quantity, null))))
                .ToList();

            if (order.ReservationId is long reservationId)
            {
                rows.AddRange(await _db.Set<ReservationService>().AsNoTracking()
                    .Where(service => service.ReservationId == reservationId)
                    .OrderBy(service => service.Id)
                    .Select(service => new CheckLine(service.NameSnapshot, service.Quantity, null))
                    .ToListAsync(cancellationToken));
            }

            Audit(id, "staff.order.print-check", "Mở bản in phiếu kiểm đồ.");
            return View("PrintCheck", new StaffCheck(order.Code,
                order.OrderTables.Select(link => link.Table.Code).Distinct().OrderBy(code => code).ToList(),
                rows));
        }, cancellationToken, id);
    }

    [HttpGet("don/{orderId:long}/hoa-don", Name = "DoraFoodStaffReceipt")]
    public async Task<IActionResult> Receipt(long orderId, CancellationToken cancellationToken)
    {
        var invoice = await ReceiptQuery(orderId).SingleOrDefaultAsync(cancellationToken);
        return invoice is null ? NotFound() : View("Receipt", invoice);
    }

    [HttpPost("don/{orderId:long}/hoa-don/in", Name = "DoraFoodStaffPrintReceipt")]
    public Task<IActionResult> PrintReceipt(long orderId, CancellationToken cancellationToken)
    {
        return WriteAtTableAsync(async () =>
        {
            var invoice = await ReceiptQuery(orderId).SingleOrDefaultAsync(cancellationToken);
            if (invoice is null) return NotFound();

            Audit(orderId, "staff.order.print-invoice", $"Mở bản in hóa đơn {invoice.Number}.");
            ViewData["PrintVersion"] = true;
            return View("Receipt", invoice);
        }, cancellationToken);
    }

    private IQueryable<Invoice> ReceiptQuery(long orderId) =>
        _db.Set<Invoice>().AsNoTracking()
            .Include(invoice => invoice.Order).Include(invoice => invoice.InvoiceLines)
            .Include(invoice => invoice.IssuedByNavigation).AsSplitQuery()
            .Where(invoice => invoice.OrderId == orderId && invoice.Order.OrderType == "DINE_IN"
                && invoice.Status == "ISSUED" && invoice.IssuedBy == _actorId);

    private Task<System.Collections.Generic.List<StaffOperation>> LoadStaffOperationsAsync(
        long orderId, CancellationToken cancellationToken)
    {
        var key = orderId.ToString(CultureInfo.InvariantCulture);
        return _db.Set<AuditLog>().AsNoTracking()
            .Where(log => log.EntityKey == key
                && ((log.EntityName == "Orders" && log.Action.StartsWith("staff.order."))
                    || (log.EntityName == "Order" && log.Action == "TABLE_CASH_CHECKOUT")))
            .OrderByDescending(log => log.Id).Take(30)
            .Select(log => new StaffOperation(log.Actor == null ? "—" : log.Actor.UserName,
                log.Summary ?? "", log.CreatedAtUtc)).ToListAsync(cancellationToken);
    }

    private void Audit(long orderId, string action, string summary)
    {
        _db.Set<AuditLog>().Add(new AuditLog
        {
            ActorId = _actorId,
            EntityName = "Orders",
            EntityKey = orderId.ToString(CultureInfo.InvariantCulture),
            Action = action,
            Summary = summary,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task<IActionResult> WriteAtTableAsync(Func<Task<IActionResult>> write,
        CancellationToken cancellationToken, long? backOrderId = null)
    {
        try
        {
            return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var transaction = await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                await _db.Database.ExecuteSqlRawAsync(TableLockSql, cancellationToken);
                var result = await write();
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (Exception ex) when (ex is DbUpdateException or SqlException)
        {
            _logger.LogWarning(ex, "Không xác nhận được thao tác phục vụ của {ActorId}.", _actorId);
            TempData.Remove("StaffSuccess");
            return StaffError("Không xác nhận được thao tác. Kiểm tra lại trước khi thử tiếp.", backOrderId);
        }
    }

    private IActionResult StaffError(string message, long? orderId = null)
    {
        TempData["StaffError"] = message;
        return orderId.HasValue
            ? RedirectToRoute("DoraFoodStaffOrder", new { id = orderId.Value })
            : RedirectToRoute("DoraFoodStaffDesk");
    }
}

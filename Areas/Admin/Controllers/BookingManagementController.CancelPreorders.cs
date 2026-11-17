using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public partial class BookingManagementController
{
    // Chuẩn bị hủy các đơn món chưa phục vụ.
    // Hàm không tự SaveChanges: lưu cùng thao tác hủy đặt bàn.
    private async Task<bool> CancelUnservedTableOrdersAsync(
        long reservationId,
        string reason)
    {
        var cancellationToken = HttpContext.RequestAborted;

        var orders = await _context.Set<Order>()
            .AsTracking()
            .Include(order => order.OrderItems)
            .Include(order => order.OrderTables)
            .Include(order => order.Invoice)
            .Include(order => order.Payments)
            .Where(order =>
                order.ReservationId == reservationId
                && order.OrderType == "DINE_IN"
                && (
                    order.Status == "OPEN"
                    || order.Status == "IN_PROGRESS"))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            return true;
        }

        // Kiểm tra tất cả đơn trước khi thay đổi bất kỳ dòng nào.
        if (orders.Any(order =>
                order.Invoice != null
                || order.ClosedAtUtc.HasValue
                || order.OrderTables.Count > 0
                || order.OrderItems.Any(
                    item => item.Status == "SERVED")))
        {
            TempData["BookingManagementError"] =
                "Lượt đặt bàn có đơn đã gắn bàn, đã phục vụ " +
                "hoặc có hóa đơn. Không thể tự hủy đơn đặt trước.";

            return false;
        }

        if (orders.Any(order =>
                order.Payments.Any(payment =>
                    payment.Status == "PENDING"
                    || payment.Status == "SUCCEEDED")))
        {
            TempData["BookingManagementError"] =
                "Đơn có khoản thu hoặc khoản thanh toán đang chờ. " +
                "Cần kiểm tra và xử lý khoản tiền trước khi hủy.";

            return false;
        }

        if (!long.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var actorId)
            || actorId <= 0)
        {
            TempData["BookingManagementError"] =
                "Không xác định được nhân viên thực hiện.";

            return false;
        }

        reason = reason.Trim();

        if (reason.Length > 500)
        {
            reason = reason[..500];
        }

        var now = DateTime.UtcNow;

        foreach (var order in orders)
        {
            var previousStatus = order.Status;

            order.Status = "CANCELLED";
            order.ClosedAtUtc = now;

            foreach (var item in order.OrderItems
                .Where(item => item.Status != "CANCELLED"))
            {
                item.Status = "CANCELLED";
            }

            // Không xóa món hoặc món thêm đi kèm.
            // Giữ nguyên số lượng, đơn giá và ghi chú.
            _context.Set<OrderStatusHistory>().Add(
                new OrderStatusHistory
                {
                    OrderId = order.Id,
                    FromStatus = previousStatus,
                    ToStatus = "CANCELLED",
                    ChangedBy = actorId,
                    ChangedAtUtc = now,
                    Reason = reason
                });

            _context.Set<AuditLog>().Add(new AuditLog
            {
                ActorId = actorId,
                Action = "TABLE_PREORDER_CANCELLED",
                EntityName = "Order",
                EntityKey = order.Id.ToString(
                    CultureInfo.InvariantCulture),
                Summary =
                    $"Hủy đơn món đặt trước {order.Code}. " +
                    $"Đặt bàn ID {reservationId}. {reason}",
                CorrelationId = Guid.NewGuid(),
                CreatedAtUtc = now
            });
        }

        return true;
    }
}
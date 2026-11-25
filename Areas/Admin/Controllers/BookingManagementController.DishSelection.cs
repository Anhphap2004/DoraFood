using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public partial class BookingManagementController
{
    public sealed class TableDishSelection
    {
        public long VariantId { get; set; }
        public int Quantity { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("/quan-ly/don-goi-mon/{orderId:long}/chon-nhieu-mon", Name = "DoraFoodSaveTableDishes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTableDishes(
        [FromRoute] long orderId, [FromForm] Guid operationId,
        [FromForm] List<TableDishSelection> items)
    {
        IActionResult Back() => RedirectToRoute("DoraFoodTableOrderDetails", new { orderId });

        if (!ModelState.IsValid || operationId == Guid.Empty || items is not { Count: > 0 and <= 100 }
            || items.Any(item => item.VariantId <= 0 || item.Quantity is < 1 or > 999
                || item.Note?.Length > 500)
            || items.Select(item => item.VariantId).Distinct().Count() != items.Count)
        {
            TempData["TableOrderError"] = "Chọn từ 1 đến 100 món, số lượng từ 1 đến 999 và ghi chú tối đa 500 ký tự.";
            return Back();
        }

        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) || actorId <= 0)
            return Forbid();

        var cancellationToken = HttpContext.RequestAborted;
        var orderKey = orderId.ToString(CultureInfo.InvariantCulture);
        const string auditAction = "TABLE_ORDER_ITEMS_ADDED";

        return await RunBookingTransactionAsync(async () =>
        {
            if (await _context.Set<AuditLog>().AnyAsync(log =>
                    log.EntityName == "Order" && log.EntityKey == orderKey
                    && log.Action == auditAction && log.ActorId == actorId
                    && log.CorrelationId == operationId, cancellationToken))
            {
                return Back();
            }

            var order = await _context.Set<Order>().AsNoTracking()
                .Include(entry => entry.Reservation)
                .Include(entry => entry.OrderTables)
                .Include(entry => entry.Invoice)
                .AsSplitQuery()
                .SingleOrDefaultAsync(entry => entry.Id == orderId && entry.OrderType == "DINE_IN",
                    cancellationToken);

            if (order is null)
                return NotFound();

            if (order.Invoice is not null || order.ClosedAtUtc.HasValue
                || !CanStaffManageTableOrder(order))
            {
                TempData["TableOrderError"] = "Đơn đã đóng hoặc lượt đặt bàn không còn nhận món.";
                return Back();
            }

            var variantIds = items.Select(item => item.VariantId).ToArray();
            var variants = await _context.Set<DishVariant>().AsNoTracking()
                .Where(variant => variantIds.Contains(variant.Id) && variant.IsAvailable
                    && variant.Dish.IsActive && variant.Price >= 0)
                .Select(variant => new { variant.Id, variant.Name, DishName = variant.Dish.Name, variant.Price })
                .ToDictionaryAsync(variant => variant.Id, cancellationToken);

            if (variants.Count != items.Count)
            {
                TempData["TableOrderError"] = "Có món đã ngừng bán. Hãy chọn lại danh sách món.";
                return Back();
            }

            var now = DateTime.UtcNow;
            foreach (var item in items)
            {
                var variant = variants[item.VariantId];
                var name = $"{variant.DishName} · {variant.Name}";
                _context.Set<OrderItem>().Add(new OrderItem
                {
                    OrderId = orderId,
                    VariantId = variant.Id,
                    NameSnapshot = name.Length > 250 ? name[..250] : name,
                    Quantity = item.Quantity,
                    UnitPrice = variant.Price,
                    Status = "NEW",
                    Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(),
                    CreatedAtUtc = now
                });
            }

            _context.Set<AuditLog>().Add(new AuditLog
            {
                ActorId = actorId,
                Action = auditAction,
                EntityName = "Order",
                EntityKey = orderKey,
                CorrelationId = operationId,
                Summary = $"Đã thêm {items.Count} dòng món, tổng {items.Sum(item => item.Quantity)} phần.",
                CreatedAtUtc = now
            });

            NotifyCustomerAboutTableOrder(order, "Nhà hàng đã ghi nhận món",
                "Quý khách có thể mở chi tiết đặt bàn để kiểm tra danh sách món.");
            await _context.SaveChangesAsync(cancellationToken);
            TempData["TableOrderSuccess"] = $"Đã lưu {items.Count} dòng món.";
            return Back();
        }, Back, "TableOrderError");
    }

    [HttpPost("/quan-ly/don-goi-mon/{orderId:long}/dong-mon/{lineId:long}/huy",
        Name = "DoraFoodCancelTableDishFromAdmin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelTableDishFromAdmin(
    [FromRoute] long orderId, [FromRoute] long lineId)
    {
        IActionResult Back() => RedirectToRoute("DoraFoodTableOrderDetails", new { orderId });

        if (!ModelState.IsValid || orderId <= 0 || lineId <= 0)
        {
            TempData["TableOrderError"] = "Thông tin món cần hủy không hợp lệ.";
            return Back();
        }

        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) || actorId <= 0)
            return Forbid();

        var cancellationToken = HttpContext.RequestAborted;
        return await RunBookingTransactionAsync(async () =>
        {
            var order = await _context.Set<Order>().AsNoTracking()
                .Include(entry => entry.Reservation)
                .Include(entry => entry.OrderTables)
                .Include(entry => entry.Invoice)
                .AsSplitQuery()
                .SingleOrDefaultAsync(entry => entry.Id == orderId && entry.OrderType == "DINE_IN",
                    cancellationToken);

            if (order is null)
                return NotFound();

            if (order.Invoice is not null || order.ClosedAtUtc.HasValue
                || !CanStaffManageTableOrder(order)
                || await _context.Set<Payment>().AnyAsync(payment => payment.OrderId == orderId
                    && payment.Purpose == "SETTLEMENT"
                    && (payment.Status == "PENDING" || payment.Status == "SUCCEEDED"), cancellationToken))
            {
                TempData["TableOrderError"] = "Đơn đang thanh toán, đã đóng hoặc lượt đặt hết hiệu lực.";
                return Back();
            }

            var lines = _context.Set<OrderItem>().Where(item => item.Id == lineId && item.OrderId == orderId);
            var line = await lines.Select(item => new { item.NameSnapshot, item.Status })
                .SingleOrDefaultAsync(cancellationToken);

            if (line is null)
                return NotFound();
            if (line.Status == "CANCELLED")
                return Back();

            var affectedRows = await lines.Where(item => item.Status != "CANCELLED")
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, "CANCELLED"),
                    cancellationToken);

            if (affectedRows != 1 || !await lines.AnyAsync(item => item.Status == "CANCELLED", cancellationToken))
                throw new DbUpdateException($"Không xác nhận được trạng thái hủy của dòng món {lineId}.");

            _context.Set<AuditLog>().Add(new AuditLog
            {
                ActorId = actorId,
                Action = "TABLE_ORDER_ITEM_CANCELLED",
                EntityName = "Order",
                EntityKey = orderId.ToString(CultureInfo.InvariantCulture),
                Summary = $"Hủy {line.NameSnapshot} (dòng {lineId}).",
                CreatedAtUtc = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(cancellationToken);
            TempData["TableOrderSuccess"] = $"Đã hủy {line.NameSnapshot}.";
            return Back();
        }, Back, "TableOrderError");
    }
}

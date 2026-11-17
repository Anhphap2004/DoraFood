using System;
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
    [HttpPost(
        "/quan-ly/don-goi-mon/{orderId:long}/mon/{itemId:long}/so-luong",
        Name = "DoraFoodUpdateTableOrderItemQuantity")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateTableOrderItemQuantity(
        [FromRoute] long orderId,
        [FromRoute] long itemId,
        [FromForm] decimal quantity,
        [FromForm] string? version)
    {
        return ChangeTableOrderItemAsync(
            orderId, itemId, version, quantity);
    }

    [HttpPost(
        "/quan-ly/don-goi-mon/{orderId:long}/mon/{itemId:long}/huy",
        Name = "DoraFoodCancelTableOrderItem")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CancelTableOrderItem(
        [FromRoute] long orderId,
        [FromRoute] long itemId,
        [FromForm] string? version)
    {
        // quantity = null nghĩa là hủy dòng món.
        return ChangeTableOrderItemAsync(
            orderId, itemId, version, null);
    }

    private async Task<IActionResult> ChangeTableOrderItemAsync(
        long orderId,
        long itemId,
        string? version,
        decimal? quantity)
    {
        IActionResult Back()
        {
            return RedirectToRoute(
                "DoraFoodTableOrderDetails",
                new { orderId });
        }

        IActionResult Error(string message)
        {
            TempData["TableOrderError"] = message;
            return Back();
        }

        if (!ModelState.IsValid || orderId <= 0 || itemId <= 0)
        {
            return BadRequest("Thông tin món không hợp lệ.");
        }

        if (quantity.HasValue
            && (
                quantity.Value < 0.001m
                || quantity.Value > 999m
                || quantity.Value !=
                    decimal.Round(quantity.Value, 3)))
        {
            return Error(
                "Số lượng từ 0,001 đến 999, " +
                "tối đa 3 chữ số thập phân.");
        }

        if (!long.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var actorId)
            || actorId <= 0)
        {
            return Forbid();
        }

        var cancellationToken = HttpContext.RequestAborted;

        return await RunBookingTransactionAsync(
            async () =>
            {
                var item = await _context.Set<OrderItem>()
                    .AsTracking()
                    .Include(row => row.OrderItemAddOns)
                    .Include(row => row.Order)
                        .ThenInclude(order => order.Reservation)
                    .Include(row => row.Order)
                        .ThenInclude(order => order.OrderTables)
                    .Include(row => row.Order)
                        .ThenInclude(order => order.Invoice)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(
                        row =>
                            row.Id == itemId
                            && row.OrderId == orderId
                            && row.Order.OrderType == "DINE_IN",
                        cancellationToken);

                if (item == null)
                {
                    return NotFound("Không tìm thấy món trong đơn.");
                }

                var order = item.Order;

                if (!CanStaffManageTableOrder(order))
                {
                    return Error(
                        "Không thể thay đổi món khi lượt đặt bàn " +
                        "không còn hợp lệ hoặc đơn đã có hóa đơn.");
                }

                if (item.Status == "CANCELLED")
                {
                    return Error("Món này đã được hủy.");
                }

                if (!MatchesTableItemVersion(item, version))
                {
                    return Error(
                        "Món đã thay đổi. " +
                        "Anh kiểm tra lại số lượng rồi thao tác lại.");
                }

                var oldQuantity = item.Quantity;
                var isCancel = !quantity.HasValue;

                if (!isCancel && quantity!.Value == oldQuantity)
                {
                    return Back();
                }

                // Kiểm tra món thêm trước khi sửa dữ liệu.
                var addOnChanges = item.OrderItemAddOns
                    .Select(addOn => new
                    {
                        Item = addOn,
                        Quantity = isCancel
                            ? addOn.Quantity
                            : addOn.Quantity
                                * quantity!.Value / oldQuantity
                    })
                    .ToList();

                if (!isCancel && addOnChanges.Any(change =>
                        change.Quantity <= 0
                        || change.Quantity >= 1_000_000_000m
                        || change.Quantity !=
                            decimal.Round(change.Quantity, 3)))
                {
                    return Error(
                        "Số lượng mới không thể phân bổ chính xác " +
                        "cho món thêm đi kèm.");
                }

                if (isCancel)
                {
                    item.Status = "CANCELLED";
                }
                else
                {
                    item.Quantity = quantity!.Value;

                    foreach (var change in addOnChanges)
                    {
                        change.Item.Quantity = change.Quantity;
                    }
                }

                // Làm mới Version của đơn, buộc bill cũ
                // phải được kiểm tra lại trước khi thu tiền.
                _context.Entry(order)
                    .Property(row => row.Status)
                    .IsModified = true;

                var before = oldQuantity.ToString(
                    "0.###", CultureInfo.InvariantCulture);

                var after = item.Quantity.ToString(
                    "0.###", CultureInfo.InvariantCulture);

                _context.Set<AuditLog>().Add(new AuditLog
                {
                    ActorId = actorId,
                    Action = isCancel
                        ? "TABLE_ORDER_ITEM_CANCELLED"
                        : "TABLE_ORDER_ITEM_UPDATED",
                    EntityName = "Order",
                    EntityKey = orderId.ToString(
                        CultureInfo.InvariantCulture),
                    Summary = isCancel
                        ? $"Hủy món ID {item.Id}: {item.NameSnapshot}."
                        : $"Món ID {item.Id}: {item.NameSnapshot}. " +
                          $"Đổi số lượng {before} → {after}.",
                    CorrelationId = Guid.NewGuid(),
                    CreatedAtUtc = DateTime.UtcNow
                });

                NotifyCustomerAboutTableOrder(
    order,
    isCancel
        ? "Nhà hàng đã hủy một món"
        : "Nhà hàng đã điều chỉnh số lượng món",
    isCancel
        ? $"Món {item.NameSnapshot} đã được hủy " +
          "và không tính vào tiền thanh toán."
        : $"Món {item.NameSnapshot} đã đổi số lượng " +
          $"từ {before} thành {after}.");
                await _context.SaveChangesAsync(cancellationToken);

                TempData["TableOrderSuccess"] = isCancel
                    ? $"Đã hủy món {item.NameSnapshot}."
                    : $"Đã cập nhật số lượng món {item.NameSnapshot}.";

                return Back();
            },
            Back,
            "TableOrderError");
    }

    private static bool MatchesTableItemVersion(
        OrderItem item,
        string? version)
    {
        if (string.IsNullOrWhiteSpace(version)
            || version.Length > 64)
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(version)
                .SequenceEqual(item.Version);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
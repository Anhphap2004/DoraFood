using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public partial class TablePaymentsController
{
    [HttpGet(
    "/quan-ly/don-goi-mon/{orderId:long}/thanh-toan",
    Name = "DoraFoodTableCheckout")]
    public async Task<IActionResult> TableCheckout(
    [FromRoute] long orderId,
    [FromQuery] string? voucherCode)
    {
        if (!ModelState.IsValid || orderId <= 0)
        {
            return BadRequest(
                "Thông tin yêu cầu không hợp lệ.");
        }

        var model = await BuildTableCheckoutModelAsync(
            orderId);

        if (model == null)
        {
            return NotFound(
                "Không tìm thấy đơn gọi món.");
        }

        await ApplyTableCheckoutVoucherAsync(
            model,
            voucherCode);

        if (model.CanCheckout)
        {
            var actorIdText = User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!long.TryParse(actorIdText, out var actorId)
                || actorId <= 0)
            {
                return Forbid();
            }

            model.ConfirmationToken =
                await CreateTableCashTokenAsync(
                    model,
                    actorId);
        }

        ViewData["Title"] =
            $"Thanh toán đơn {model.OrderCode}";

        return View("TableCheckout", model);
    }

    private async Task<TableCheckoutViewModel?>
        BuildTableCheckoutModelAsync(long orderId)
    {
        var cancellationToken = HttpContext.RequestAborted;

        var order = await _context.Set<Order>()
            .AsNoTracking()
            .Include(item => item.Reservation)
            .Include(item => item.OrderTables)
                .ThenInclude(link => link.Table)
            .Include(item => item.OrderItems)
                .ThenInclude(item => item.Variant)
            .Include(item => item.OrderItems)
                .ThenInclude(item => item.OrderItemAddOns)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                item => item.Id == orderId
                    && item.OrderType == "DINE_IN",
                cancellationToken);

        if (order is null)
        {
            return null;
        }

        var lines = new List<TableCheckoutLineRow>();

        // 1. Tiền món và topping.
        // Món đã hủy không đưa vào thanh toán.
        foreach (var item in order.OrderItems
            .Where(item => item.Status != "CANCELLED")
            .OrderBy(item => item.Id))
        {
            lines.Add(new TableCheckoutLineRow(
                "DISH",
                item.Id,
                item.NameSnapshot,
                item.Variant.SellingUnit,
                item.Quantity,
                item.UnitPrice));

            foreach (var addOn in item.OrderItemAddOns
                .OrderBy(addOn => addOn.Id))
            {
                // Quantity của topping đã là tổng số lượng của cả dòng.
                // Không nhân thêm với số lượng món.
                lines.Add(new TableCheckoutLineRow(
                    "ADDON",
                    item.Id,
                    $"{addOn.NameSnapshot} · {item.NameSnapshot}",
                    "phần",
                    addOn.Quantity,
                    addOn.UnitPrice));
            }
        }

        // 2. Dịch vụ đã lưu cho lượt đặt bàn.
        if (order.ReservationId.HasValue)
        {
            var services = await _context.Set<ReservationService>()
                .AsNoTracking()
                .Where(service =>
                    service.ReservationId == order.ReservationId.Value)
                .OrderBy(service => service.Id)
                .Select(service => new TableCheckoutLineRow(
                    "SERVICE",
                    null,
                    service.NameSnapshot,
                    "lượt",
                    service.Quantity,
                    service.UnitPrice))
                .ToListAsync(cancellationToken);

            lines.AddRange(services);
        }

        // 3. Khoản giảm thực tế đã được ghi nhận.
        var discounts = await _context.Set<OrderDiscount>()
            .AsNoTracking()
            .Where(discount => discount.OrderId == order.Id)
            .OrderBy(discount => discount.Id)
            .Select(discount => new TableCheckoutDiscountRow(
                discount.Description,
                discount.Amount))
            .ToListAsync(cancellationToken);

        // 4. Tất cả khoản đã thu thành công, gồm cả tiền cọc.
        var payments = _context.Set<Payment>()
            .AsNoTracking()
            .Where(payment => payment.OrderId == order.Id);

        var receivedAmount = await payments
            .Where(payment => payment.Status == "SUCCEEDED")
            .SumAsync(
                payment => (decimal?)payment.Amount,
                cancellationToken)
            ?? 0m;

        // 5. Khoản hoàn thành công của các thanh toán trên đơn.
        var refundedAmount = await (
            from refund in _context.Set<Refund>().AsNoTracking()
            join payment in payments
                on refund.PaymentId equals payment.Id
            where refund.Status == "SUCCEEDED"
            select (decimal?)refund.Amount)
            .SumAsync(cancellationToken)
            ?? 0m;

        var hasPendingPayment = await payments
            .AnyAsync(
                payment => payment.Status == "PENDING",
                cancellationToken);

        var hasInvoice = await _context.Set<Invoice>()
            .AsNoTracking()
            .AnyAsync(
                invoice => invoice.OrderId == order.Id,
                cancellationToken);

        // Tính theo dữ liệu của đơn hiện tại.
        // Thuế sẽ được xử lý theo hóa đơn khi nối phần chốt thanh toán.
        var subtotal = lines.Sum(line => line.LineTotal);
        var discountAmount = discounts.Sum(discount => discount.Amount);
        var totalAmount = subtotal - discountAmount;
        var netReceivedAmount = receivedAmount - refundedAmount;

        string? blockReason = null;

        if ((order.Status != "OPEN"
                && order.Status != "IN_PROGRESS")
            || order.ClosedAtUtc.HasValue)
        {
            blockReason = "Đơn đã đóng hoặc không còn ở trạng thái phục vụ.";
        }
        else if (order.ReservationId.HasValue
            && order.Reservation?.Status != "SEATED")
        {
            blockReason = "Lượt đặt bàn không còn ở trạng thái khách đang sử dụng bàn.";
        }
        else if (!order.OrderTables.Any(
            link => link.ReleasedAtUtc == null))
        {
            blockReason = "Đơn không còn bàn đang sử dụng.";
        }
        else if (hasInvoice)
        {
            blockReason =
                "Đơn đã có hóa đơn. Cần kiểm tra hóa đơn hiện tại trước khi thu thêm.";
        }
        else if (hasPendingPayment)
        {
            blockReason =
                "Đơn còn khoản thanh toán đang chờ xử lý. "
                + "Cần kiểm tra khoản đó trước khi thu thêm tiền.";
        }
        else if (lines.Count == 0 || subtotal <= 0m)
        {
            blockReason = "Đơn chưa có món hoặc dịch vụ để thanh toán.";
        }
        else if (discountAmount < 0m || discountAmount > subtotal)
        {
            blockReason = "Khoản giảm giá của đơn không hợp lệ.";
        }
        else if (netReceivedAmount < 0m)
        {
            blockReason = "Tiền hoàn đang vượt tiền đã thu. Cần kiểm tra lại.";
        }
        else if (netReceivedAmount > totalAmount)
        {
            blockReason =
                "Số tiền đã nhận đang vượt tổng tiền đơn. "
                + "Cần kiểm tra và xử lý khoản chênh lệch.";
        }

        ViewData["TableCheckoutBlockReason"] = blockReason;

        return new TableCheckoutViewModel
        {
            OrderId = order.Id,
            OrderCode = order.Code,
            OrderStatus = order.Status,

            ReservationId = order.ReservationId,
            ReservationCode = order.Reservation?.Code,

            GuestName = order.GuestName,
            GuestPhone = order.GuestPhone,

            Tables = order.OrderTables
                .OrderBy(link => link.Table.Code)
                .Select(link => link.Table.Code)
                .Distinct()
                .ToList(),

            Lines = lines,
            Discounts = discounts,

            TaxAmount = 0m,
            ReceivedAmount = receivedAmount,
            RefundedAmount = refundedAmount,

            CanCheckout = blockReason is null
        };
    }
}
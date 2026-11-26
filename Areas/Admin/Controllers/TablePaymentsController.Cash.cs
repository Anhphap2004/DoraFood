using DoraFood.Areas.Admin.Filters;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DoraFood.Controllers;

public partial class TablePaymentsController
{
    // Nội dung xác nhận được mã hóa, chống sửa từ trình duyệt.
    private sealed record TableCashConfirmation(
        long OrderId,
        long ActorId,
        Guid OperationId,
        DateTime ExpiresAtUtc,
        string? VoucherCode,
        string Fingerprint,
        // Các mã xác nhận cũ cấp cho luồng phục vụ phải bị từ chối.
        bool StaffCheckout = false);

    private IDataProtector TableCashProtector =>
        HttpContext.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("DoraFood.TableCashCheckout.v1");

    // Dùng chung cho GET xem trước và POST thu tiền.
    private async Task ApplyTableCheckoutVoucherAsync(
        TableCheckoutViewModel model,
        string? voucherCode)
    {
        var cancellationToken = HttpContext.RequestAborted;

        model.VoucherCode = voucherCode?
            .Trim()
            .ToUpperInvariant();

        model.HasSavedVoucher = await _context.Set<OrderDiscount>()
            .AsNoTracking()
            .AnyAsync(
                discount =>
                    discount.OrderId == model.OrderId
                    && discount.VoucherId.HasValue,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(model.VoucherCode))
        {
            return;
        }

        if (!model.CanCheckout)
        {
            model.VoucherError =
                "Đơn chưa đủ điều kiện thanh toán nên chưa thể áp mã.";
        }
        else if (model.HasSavedVoucher)
        {
            model.VoucherError =
                "Đơn đã có voucher được lưu. Không thể áp thêm mã.";
        }
        else
        {
            var customerId = await _context.Set<Order>()
                .AsNoTracking()
                .Where(order => order.Id == model.OrderId)
                .Select(order => order.CustomerId)
                .SingleAsync(cancellationToken);

            var voucherBase = Math.Max(
                0m,
                model.Subtotal - model.DiscountAmount);

            var result = await CheckTableVoucherAsync(
                model.VoucherCode,
                voucherBase,
                model.OrderId,
                customerId,
                cancellationToken);

            if (!result.IsValid)
            {
                model.VoucherError = result.ErrorMessage;
            }
            else
            {
                model.VoucherCode = result.Code;

                model.PreviewVoucherDiscount =
                    result.DiscountAmount;

                if (model.OverpaidAmount > 0)
                {
                    model.PreviewVoucherDiscount = 0m;

                    model.VoucherError =
                        "Tổng tiền sau giảm thấp hơn số tiền đã thu. " +
                        "Cần xử lý tiền đã thu trước khi áp mã.";
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(model.VoucherError))
        {
            model.CanCheckout = false;
        }
    }

    // So sánh nội dung bill khi mở trang với lúc xác nhận.
    // Không chỉ kiểm tra Order.Version vì thêm món có thể
    // chỉ tạo OrderItem mà không đổi Version của Order.
    private async Task<string> GetTableCashFingerprintAsync(
        TableCheckoutViewModel model)
    {
        var cancellationToken = HttpContext.RequestAborted;

        var orderVersion = await _context.Set<Order>()
            .AsNoTracking()
            .Where(order => order.Id == model.OrderId)
            .Select(order => order.Version)
            .SingleAsync(cancellationToken);

        var activeTableIds = await _context.Set<OrderTable>()
            .AsNoTracking()
            .Where(link =>
                link.OrderId == model.OrderId
                && link.ReleasedAtUtc == null)
            .OrderBy(link => link.TableId)
            .Select(link => link.TableId)
            .ToListAsync(cancellationToken);

        var snapshot = new
        {
            model.OrderId,
            Version = Convert.ToBase64String(orderVersion),
            model.ReservationId,
            model.OrderStatus,
            model.GuestName,
            model.GuestPhone,
            ActiveTableIds = activeTableIds,
            model.Lines,
            model.Discounts,
            model.VoucherCode,
            model.PreviewVoucherDiscount,
            model.TaxAmount,
            model.TotalAmount,
            model.ReceivedAmount,
            model.RefundedAmount,
            model.AmountToCollect
        };

        var json = JsonSerializer.Serialize(snapshot);

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(hash);
    }

    private async Task<string> CreateTableCashTokenAsync(
        TableCheckoutViewModel model,
        long actorId)
    {
        var confirmation = new TableCashConfirmation(
            model.OrderId,
            actorId,
            Guid.NewGuid(),
            DateTime.UtcNow.AddMinutes(15),
            model.VoucherCode,
            await GetTableCashFingerprintAsync(model));

        return TableCashProtector.Protect(
            JsonSerializer.Serialize(confirmation));
    }

    private TableCashConfirmation? ReadTableCashToken(
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > 16000)
        {
            return null;
        }

        try
        {
            var json = TableCashProtector.Unprotect(token);

            return JsonSerializer.Deserialize<TableCashConfirmation>(
                json);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [HttpPost(
        "/quan-ly/don-goi-mon/{orderId:long}/thu-tien-mat",
        Name = "DoraFoodConfirmTableCash")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmTableCash(
        [FromRoute] long orderId,
        [FromForm] string? confirmationToken)
    {
        if (!ModelState.IsValid || orderId <= 0)
        {
            return BadRequest("Thông tin yêu cầu không hợp lệ.");
        }

        if (!long.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var actorId)
            || actorId <= 0)
        {
            return Forbid();
        }

        var confirmation = ReadTableCashToken(
            confirmationToken);

        if (confirmation == null
            || confirmation.StaffCheckout
            || confirmation.OrderId != orderId
            || confirmation.ActorId != actorId
            || confirmation.OperationId == Guid.Empty)
        {
            TempData["TableOrderError"] =
                "Thông tin xác nhận không hợp lệ. " +
                "Anh hãy mở lại trang thanh toán.";

            return RedirectToRoute(
                "DoraFoodTableCheckout",
                new { orderId });
        }

        IActionResult AfterCheckout() => RedirectToRoute(
    "DoraFoodTableInvoice",
    new { orderId });

        var cancellationToken = HttpContext.RequestAborted;

        var orderKey = orderId.ToString(
            CultureInfo.InvariantCulture);

        IActionResult BackWithError(string message)
        {
            TempData["TableOrderError"] = message;

            return RedirectToRoute(
                "DoraFoodTableCheckout",
                new
                {
                    orderId,
                    voucherCode = confirmation.VoucherCode
                });
        }

        return await RunBookingTransactionAsync(
            async () =>
            {
                // Gửi lại cùng một yêu cầu đã thành công:
                // chỉ thông báo, không tạo khoản thu thứ hai.
                var alreadyProcessed = await _context.Set<AuditLog>()
                    .AsNoTracking()
                    .AnyAsync(
                        log =>
                            log.CorrelationId ==
                                confirmation.OperationId
                            && log.ActorId == actorId
                            && log.Action == "TABLE_CASH_CHECKOUT"
                            && log.EntityName == "Order"
                            && log.EntityKey == orderKey,
                        cancellationToken);

                if (alreadyProcessed)
                {
                    TempData["TableOrderSuccess"] =
                        "Đơn này đã được xác nhận thanh toán.";

                    return AfterCheckout();
                }

                if (confirmation.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    return BackWithError(
                        "Trang thanh toán đã hết thời gian xác nhận. " +
                        "Anh hãy kiểm tra lại bill rồi xác nhận lại.");
                }

                // Khóa dòng voucher trước khi kiểm tra lượt.
                // Khóa được giữ đến khi giao dịch hoàn tất.
                if (!string.IsNullOrWhiteSpace(
                        confirmation.VoucherCode))
                {
                    await _context.Set<Voucher>()
                        .FromSqlInterpolated($"""
                            SELECT *
                            FROM dbo.Vouchers WITH (UPDLOCK, HOLDLOCK)
                            WHERE Code = {confirmation.VoucherCode}
                            """)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);
                }

                // Dựng lại bill từ dữ liệu hiện tại.
                var model = await BuildTableCheckoutModelAsync(
                    orderId);

                if (model == null)
                {
                    return NotFound("Không tìm thấy đơn gọi món.");
                }

                await ApplyTableCheckoutVoucherAsync(
                    model,
                    confirmation.VoucherCode);

                if (!model.CanCheckout)
                {
                    var message =
                        model.VoucherError
                        ?? ViewData["TableCheckoutBlockReason"] as string
                        ?? "Đơn chưa đủ điều kiện thanh toán.";

                    return BackWithError(message);
                }

                var currentFingerprint =
                    await GetTableCashFingerprintAsync(model);

                if (currentFingerprint != confirmation.Fingerprint)
                {
                    return BackWithError(
                        "Bill đã thay đổi sau khi mở trang thanh toán. " +
                        "Anh hãy kiểm tra số tiền mới rồi xác nhận lại.");
                }

                var order = await _context.Set<Order>()
                    .AsTracking()
                    .Include(item => item.Reservation)
                    .Include(item => item.OrderTables)
                    .SingleAsync(
                        item => item.Id == orderId,
                        cancellationToken);

                // Không chốt một bill khi khoản hoàn tiền
                // của bill vẫn đang chờ xử lý.
                var hasPendingRefund = await _context.Set<Refund>()
                    .AsNoTracking()
                    .AnyAsync(
                        refund =>
                            refund.Payment.OrderId == orderId
                            && refund.Status == "PENDING",
                        cancellationToken);

                if (hasPendingRefund)
                {
                    return BackWithError(
                        "Đơn đang có khoản hoàn tiền chờ xử lý.");
                }

                // Dịch vụ đặt bàn đang được tính vào bill này.
                // Nếu cùng lượt đặt bàn có bill khác, cần kiểm tra
                // trước để tránh tính dịch vụ hai lần.
                if (order.ReservationId.HasValue)
                {
                    var hasOtherBill = await _context.Set<Order>()
                        .AsNoTracking()
                        .AnyAsync(
                            other =>
                                other.Id != orderId
                                && other.ReservationId ==
                                    order.ReservationId
                                && other.Status != "CANCELLED"
                                && (
                                    other.Status == "OPEN"
                                    || other.Status == "IN_PROGRESS"
                                    || other.Invoice != null
                                    || other.OrderTables.Any(
                                        link =>
                                            link.ReleasedAtUtc == null)
                                ),
                            cancellationToken);

                    if (hasOtherBill)
                    {
                        return BackWithError(
                            "Lượt đặt bàn này còn đơn hoặc hóa đơn khác. " +
                            "Cần kiểm tra để tránh tính trùng dịch vụ.");
                    }
                }

                // Kiểm tra lại voucher đã được lưu từ trước,
                // nếu đơn đang có một voucher như vậy.
                var savedVouchers = await _context
                    .Set<OrderDiscount>()
                    .AsNoTracking()
                    .Where(discount =>
                        discount.OrderId == orderId
                        && discount.VoucherId.HasValue)
                    .ToListAsync(cancellationToken);

                if (savedVouchers.Count > 1)
                {
                    return BackWithError(
                        "Đơn có nhiều voucher đã lưu. " +
                        "Cần kiểm tra lại trước khi thu tiền.");
                }

                if (savedVouchers.Count == 1)
                {
                    var savedDiscount = savedVouchers[0];
                    var savedVoucherId =
                        savedDiscount.VoucherId!.Value;

                    var lockedVouchers = await _context.Set<Voucher>()
                        .FromSqlInterpolated($"""
                            SELECT *
                            FROM dbo.Vouchers WITH (UPDLOCK, HOLDLOCK)
                            WHERE Id = {savedVoucherId}
                            """)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);

                    var savedVoucher = lockedVouchers
                        .SingleOrDefault();

                    if (savedVoucher == null)
                    {
                        return BackWithError(
                            "Voucher đã lưu không còn tồn tại.");
                    }

                    var otherDiscountAmount =
                        model.DiscountAmount
                        - savedDiscount.Amount;

                    var savedResult =
                        await CheckTableVoucherAsync(
                            savedVoucher.Code,
                            model.Subtotal - otherDiscountAmount,
                            orderId,
                            order.CustomerId,
                            cancellationToken);

                    if (!savedResult.IsValid
                        || savedResult.DiscountAmount !=
                            savedDiscount.Amount)
                    {
                        return BackWithError(
                            savedResult.ErrorMessage
                            ?? "Khoản giảm voucher đã lưu " +
                               "không còn khớp với bill hiện tại.");
                    }
                }

                PaymentMethod? cashMethod = null;

                if (model.AmountToCollect > 0)
                {
                    cashMethod = await _context
                        .Set<PaymentMethod>()
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            method =>
                                method.Code == "CASH"
                                && method.IsActive,
                            cancellationToken);

                    if (cashMethod == null)
                    {
                        return BackWithError(
                            "Chưa có phương thức CASH đang hoạt động.");
                    }
                }

                long? newVoucherId = null;

                if (model.PreviewVoucherDiscount > 0)
                {
                    newVoucherId = await _context.Set<Voucher>()
                        .AsNoTracking()
                        .Where(voucher =>
                            voucher.Code == model.VoucherCode)
                        .Select(voucher => (long?)voucher.Id)
                        .SingleOrDefaultAsync(cancellationToken);

                    if (!newVoucherId.HasValue)
                    {
                        return BackWithError(
                            "Không tìm thấy voucher đang áp dụng.");
                    }
                }

                // Từ đây trở xuống mới bắt đầu thay đổi dữ liệu.
                // Các dữ liệu được lưu cùng một giao dịch.
                var now = DateTime.UtcNow;
                var previousStatus = order.Status;
                var invoice = new Invoice
                {
                    OrderId = order.Id,
                    Number = "HD-" + Guid.NewGuid().ToString("N"),
                    Status = "DRAFT",
                    CustomerName = order.GuestName,
                    Subtotal = model.Subtotal,
                    DiscountAmount = model.DiscountAmount,
                    TaxAmount = 0m,
                    IssuedAtUtc = null,
                    IssuedBy = actorId
                };

                // Phân bổ giảm giá vào các dòng hóa đơn.
                // Tổng giảm của các dòng phải bằng giảm của bill.
                decimal cumulativeSubtotal = 0m;
                decimal allocatedDiscount = 0m;
                var lineNumber = 0;

                foreach (var line in model.Lines)
                {
                    lineNumber++;
                    cumulativeSubtotal += line.LineTotal;

                    var cumulativeDiscount =
                        lineNumber == model.Lines.Count
                            ? model.DiscountAmount
                            : decimal.Round(
                                model.DiscountAmount
                                * cumulativeSubtotal
                                / model.Subtotal,
                                2,
                                MidpointRounding.AwayFromZero);

                    var lineDiscount =
                        cumulativeDiscount - allocatedDiscount;

                    allocatedDiscount = cumulativeDiscount;

                    var description = line.Name;

                    if (description.Length > 250)
                    {
                        description = description[..250];
                    }

                    invoice.InvoiceLines.Add(new InvoiceLine
                    {
                        LineNumber = lineNumber,
                        LineType = line.LineType,
                        OrderItemId = line.OrderItemId,
                        Description = description,
                        Unit = line.Unit,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice,
                        DiscountAmount = lineDiscount,
                        TaxRate = 0m,
                        TaxAmount = 0m
                    });
                }

                _context.Set<Invoice>().Add(invoice);

                if (newVoucherId.HasValue)
                {
                    _context.Set<OrderDiscount>().Add(
                        new OrderDiscount
                        {
                            OrderId = order.Id,
                            VoucherId = newVoucherId.Value,
                            Description =
                                $"Voucher {model.VoucherCode}",
                            Amount = model.PreviewVoucherDiscount,
                            ApprovedBy = actorId
                        });
                }

                // Nếu tiền cọc đã đủ thì không tạo Payment = 0.
                if (model.AmountToCollect > 0)
                {
                    _context.Set<Payment>().Add(new Payment
                    {
                        OrderId = order.Id,
                        MethodId = cashMethod!.Id,
                        Purpose = "SETTLEMENT",
                        Amount = model.AmountToCollect,
                        Status = "SUCCEEDED",
                        IdempotencyKey =
                            confirmation.OperationId,
                        ReceivedBy = actorId,
                        CreatedAtUtc = now,
                        PaidAtUtc = now
                    });
                }

                order.Status = "COMPLETED";
                order.ClosedAtUtc = now;

                foreach (var link in order.OrderTables
                    .Where(link => link.ReleasedAtUtc == null))
                {
                    link.ReleasedAtUtc = now;
                }

                if (order.Reservation != null)
                {
                    order.Reservation.Status = "COMPLETED";

                    _context.Set<AuditLog>().Add(new AuditLog
                    {
                        ActorId = actorId,
                        Action = "BOOKING_COMPLETED",
                        EntityName = "Reservation",
                        EntityKey = order.Reservation.Id.ToString(CultureInfo.InvariantCulture),
                        CorrelationId = confirmation.OperationId,
                        CreatedAtUtc = now,
                        Summary = $"Hoàn tất sau thanh toán đơn {order.Code}."
                    });
                }

                _context.Set<OrderStatusHistory>().Add(
                    new OrderStatusHistory
                    {
                        OrderId = order.Id,
                        FromStatus = previousStatus,
                        ToStatus = "COMPLETED",
                        ChangedBy = actorId,
                        ChangedAtUtc = now,
                        Reason =
                            "Hoàn tất thanh toán tại bàn."
                    });

                _context.Set<AuditLog>().Add(new AuditLog
                {
                    ActorId = actorId,
                    Action = "TABLE_CASH_CHECKOUT",
                    EntityName = "Order",
                    EntityKey = orderKey,
                    CorrelationId = confirmation.OperationId,
                    CreatedAtUtc = now,
                    Summary =
                        $"Hóa đơn {invoice.Number}; " +
                        $"tổng tiền {model.TotalAmount.ToString(
                            "0.00", CultureInfo.InvariantCulture)}; " +
                        $"thu thêm {model.AmountToCollect.ToString(
                            "0.00", CultureInfo.InvariantCulture)}; " +
                        $"voucher {model.VoucherCode ?? "không"}."
                });
                // Lưu hóa đơn nháp cùng các dòng chi tiết,
                // khoản thu, voucher và trạng thái phục vụ.
                // Giao dịch hiện tại chưa được commit.
                await _context.SaveChangesAsync(
                    cancellationToken);

                // Chỉ xuất hóa đơn sau khi các dòng đã lưu thành công.
                invoice.Status = "ISSUED";
                invoice.IssuedAtUtc = now;

                var customerId = order.CustomerId
                    ?? order.Reservation?.CustomerId;

                if (customerId.HasValue)
                {
                    var totalText = model.TotalAmount.ToString(
                        "N0",
                        CultureInfo.GetCultureInfo("vi-VN"));

                    _notifications.Add(
                        customerId.Value,
                        "Thanh toán tại bàn thành công",
                        $"Đơn {order.Code} đã được thanh toán đầy đủ. " +
                        $"Tổng hóa đơn: {totalText} ₫. " +
                        $"Số hóa đơn: {invoice.Number}. " +
                        "DoraFood cảm ơn quý khách!",
                        order.ReservationId.HasValue
                            ? Url.RouteUrl(
                                "DoraFoodMyBookingInvoice",
                                new
                                {
                                    area = "",
                                    id = order.ReservationId.Value,
                                    orderId = order.Id
                                }) ?? "/dat-ban/cua-toi"
                            : "/dat-ban/cua-toi");
                }

                // Xuất hóa đơn và lưu thông báo trong cùng giao dịch.
                await _context.SaveChangesAsync(
                    cancellationToken);

                // Giữ nguyên phần thông báo thành công phía dưới.
                TempData["TableOrderSuccess"] =
                                    model.AmountToCollect > 0
                        ? "Đã ghi nhận thu tiền, hoàn tất đơn " +
                          "và giải phóng bàn."
                        : "Đơn đã được thanh toán đủ trước đó. " +
                          "Đã hoàn tất đơn và giải phóng bàn.";

                return AfterCheckout();
            },
            () => RedirectToRoute(
                "DoraFoodTableCheckout",
                new
                {
                    orderId,
                    voucherCode = confirmation.VoucherCode
                }),
            "TableOrderError");
    }
}
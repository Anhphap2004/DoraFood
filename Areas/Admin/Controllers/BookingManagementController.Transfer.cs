using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public partial class BookingManagementController
{
    // GET: mở form chuyển bàn.
    [HttpGet(
        "/quan-ly/dat-ban/{id:long}/chuyen-ban",
        Name = "DoraFoodTransferBooking")]
    public async Task<IActionResult> Transfer(
        [FromRoute] long id)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return BadRequest("Thông tin đặt bàn không hợp lệ.");
        }

        var cancellationToken = HttpContext.RequestAborted;

        var reservation = await _context.Set<Reservation>()
            .AsNoTracking()
            .Include(item => item.Tables)
                .ThenInclude(table => table.Area)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (reservation == null)
        {
            return NotFound("Không tìm thấy lượt đặt bàn.");
        }

        if (reservation.Status != "SEATED")
        {
            TempData["BookingManagementError"] =
                "Chỉ chuyển bàn khi khách đang dùng bữa.";

            return RedirectToRoute(
                "DoraFoodManageBookingDetails",
                new { id });
        }

        var orders = await _context.Set<Order>()
            .AsNoTracking()
            .Where(order =>
                order.ReservationId == id
                && order.OrderType == "DINE_IN"
                && (
                    order.Status == "OPEN"
                    || order.Status == "IN_PROGRESS"))
            .OrderBy(order => order.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        var hasClosedBill = await HasClosedTransferBillAsync(id);

        if (orders.Count > 1 || hasClosedBill)
        {
            TempData["BookingManagementError"] =
                orders.Count > 1
                    ? "Lượt khách có nhiều đơn đang mở. " +
                      "Cần kiểm tra trước khi chuyển bàn."
                    : "Lượt khách đã có hóa đơn hoặc đơn hoàn tất. " +
                      "Không thể chuyển bàn.";

            return RedirectToRoute(
                "DoraFoodManageBookingDetails",
                new { id });
        }

        var activeOrder = orders.SingleOrDefault();

        var model = new TransferBookingViewModel
        {
            Id = id,
            Version = Convert.ToBase64String(
                reservation.Version),

            // Mặc định giữ các bàn hiện tại.
            // Nhân viên bỏ bàn cũ và chọn bàn mới trên form.
            TableIds = reservation.Tables
                .Select(table => table.Id)
                .ToList()
        };

        await LoadTransferFormAsync(
            model,
            reservation,
            activeOrder);

        return View("Transfer", model);
    }

    // POST: kiểm tra và lưu việc chuyển bàn.
    [HttpPost("/quan-ly/dat-ban/{id:long}/chuyen-ban")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(
        [FromRoute] long id,
        TransferBookingViewModel model)
    {
        if (id <= 0)
        {
            return BadRequest("Thông tin đặt bàn không hợp lệ.");
        }

        model.Id = id;
        model.Reason = (model.Reason ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(model.Reason))
        {
            ModelState.AddModelError(
                nameof(model.Reason),
                "Anh hãy nhập lý do chuyển bàn.");
        }

        var selectedIds = (model.TableIds ?? new List<long>())
            .Distinct()
            .OrderBy(tableId => tableId)
            .ToArray();

        if (selectedIds.Length == 0)
        {
            ModelState.AddModelError(
                nameof(model.TableIds),
                "Phải chọn ít nhất một bàn sau khi chuyển.");
        }
        else if (selectedIds.Length > 200
            || selectedIds.Any(tableId => tableId <= 0))
        {
            ModelState.AddModelError(
                nameof(model.TableIds),
                "Danh sách bàn được chọn không hợp lệ.");
        }

        var cancellationToken = HttpContext.RequestAborted;

        IActionResult BackToDetails()
        {
            return RedirectToRoute(
                "DoraFoodManageBookingDetails",
                new { id });
        }

        // Dùng cùng giao dịch và khóa với nhận khách,
        // mở đơn, thêm món và thanh toán.
        return await RunBookingTransactionAsync(
            async () =>
            {
                var reservation = await _context
                    .Set<Reservation>()
                    .AsTracking()
                    .Include(item => item.Tables)
                        .ThenInclude(table => table.Area)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(
                        item => item.Id == id,
                        cancellationToken);

                if (reservation == null)
                {
                    return NotFound(
                        "Không tìm thấy lượt đặt bàn.");
                }

                if (reservation.Status != "SEATED")
                {
                    TempData["BookingManagementError"] =
                        "Lượt khách không còn ở trạng thái đang dùng bữa.";

                    return BackToDetails();
                }

                if (!IsCurrentVersion(
                        reservation,
                        model.Version))
                {
                    TempData["BookingManagementError"] =
                        "Thông tin đặt bàn đã thay đổi. " +
                        "Anh hãy mở lại form chuyển bàn.";

                    return RedirectToRoute(
                        "DoraFoodTransferBooking",
                        new { id });
                }

                var orders = await _context.Set<Order>()
                    .AsTracking()
                    .Include(order => order.OrderTables)
                    .Where(order =>
                        order.ReservationId == id
                        && order.OrderType == "DINE_IN"
                        && (
                            order.Status == "OPEN"
                            || order.Status == "IN_PROGRESS"))
                    .OrderBy(order => order.Id)
                    .Take(2)
                    .AsSplitQuery()
                    .ToListAsync(cancellationToken);

                if (orders.Count > 1)
                {
                    TempData["BookingManagementError"] =
                        "Lượt khách có nhiều đơn đang mở. " +
                        "Cần kiểm tra trước khi chuyển bàn.";

                    return BackToDetails();
                }

                if (await HasClosedTransferBillAsync(id))
                {
                    TempData["BookingManagementError"] =
                        "Lượt khách đã có hóa đơn hoặc đơn hoàn tất. " +
                        "Không thể chuyển bàn.";

                    return BackToDetails();
                }

                var activeOrder = orders.SingleOrDefault();

                await LoadTransferFormAsync(
                    model,
                    reservation,
                    activeOrder);

                if (!ModelState.IsValid)
                {
                    return View("Transfer", model);
                }

                var currentIds = reservation.Tables
                    .Select(table => table.Id)
                    .ToHashSet();

                var targetIds = selectedIds.ToHashSet();

                if (currentIds.SetEquals(targetIds))
                {
                    ModelState.AddModelError(
                        nameof(model.TableIds),
                        "Danh sách bàn chưa thay đổi.");

                    return View("Transfer", model);
                }

                // Dữ liệu bàn trên lượt đặt bàn và đơn gọi món
                // phải khớp trước khi thay đổi.
                if (activeOrder != null)
                {
                    var orderTableIds = activeOrder.OrderTables
                        .Where(link =>
                            link.ReleasedAtUtc == null)
                        .Select(link => link.TableId)
                        .ToHashSet();

                    if (!currentIds.SetEquals(orderTableIds))
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            "Bàn của đơn gọi món chưa khớp với lượt đặt bàn. " +
                            "Cần kiểm tra dữ liệu trước khi chuyển.");

                        return View("Transfer", model);
                    }
                }

                // Danh sách này vừa được kiểm tra lại
                // bên trong giao dịch.
                var availableIds = model.Tables
                    .Where(table => table.IsAvailable)
                    .Select(table => table.Id)
                    .ToHashSet();

                if (selectedIds.Any(
                        tableId => !availableIds.Contains(tableId)))
                {
                    ModelState.AddModelError(
                        nameof(model.TableIds),
                        "Có bàn không còn khả dụng. " +
                        "Anh hãy chọn lại.");

                    return View("Transfer", model);
                }

                var selectedTables = await _context
                    .Set<DiningTable>()
                    .AsTracking()
                    .Where(table =>
                        selectedIds.Contains(table.Id)
                        && table.IsActive
                        && table.Area.IsActive)
                    .OrderBy(table => table.Code)
                    .ToListAsync(cancellationToken);

                if (selectedTables.Count != selectedIds.Length)
                {
                    ModelState.AddModelError(
                        nameof(model.TableIds),
                        "Có bàn hoặc khu vực đã ngừng hoạt động.");

                    return View("Transfer", model);
                }

                var totalCapacity = selectedTables.Sum(
                    table => (int)table.Capacity);

                if (totalCapacity < reservation.PartySize)
                {
                    ModelState.AddModelError(
                        nameof(model.TableIds),
                        $"Các bàn được chọn chỉ có {totalCapacity} chỗ, " +
                        $"chưa đủ cho {reservation.PartySize} khách.");

                    return View("Transfer", model);
                }

                var oldTableText = TransferTableNames(
                    reservation.Tables.Select(table => table.Code));

                var newTableText = TransferTableNames(
                    selectedTables.Select(table => table.Code));

                var now = DateTime.UtcNow;

                // Bắt đầu thay đổi dữ liệu sau khi kiểm tra xong.
                if (activeOrder != null)
                {
                    // Chỉ giải phóng những bàn bị bỏ khỏi lựa chọn.
                    // Bàn được giữ lại không tạo thêm dòng lịch sử.
                    foreach (var link in activeOrder.OrderTables
                        .Where(link =>
                            link.ReleasedAtUtc == null
                            && !targetIds.Contains(link.TableId)))
                    {
                        link.ReleasedAtUtc = now;
                    }

                    // Tạo phiên bản mới cho đơn để các form cũ
                    // nhận biết thông tin đã thay đổi.
                    _context.Entry(activeOrder)
                        .Property(order => order.Status)
                        .IsModified = true;
                }

                // Cập nhật danh sách bàn hiện tại của lượt khách.
                foreach (var table in reservation.Tables
                    .Where(table => !targetIds.Contains(table.Id))
                    .ToList())
                {
                    reservation.Tables.Remove(table);
                }

                foreach (var table in selectedTables
                    .Where(table => !currentIds.Contains(table.Id)))
                {
                    reservation.Tables.Add(table);
                }

                // Trạng thái vẫn là SEATED, nhưng cần cập nhật
                // rowversion để chặn gửi lại form chuyển bàn cũ.
                _context.Entry(reservation)
                    .Property(item => item.Status)
                    .IsModified = true;

                // Lưu việc giải phóng bàn trước.
                // Chưa commit giao dịch.
                await _context.SaveChangesAsync(
                    cancellationToken);

                if (activeOrder != null)
                {
                    foreach (var tableId in selectedIds
                        .Where(tableId =>
                            !currentIds.Contains(tableId)))
                    {
                        _context.Set<OrderTable>().Add(
                            new OrderTable
                            {
                                OrderId = activeOrder.Id,
                                TableId = tableId,
                                AssignedAtUtc = now
                            });
                    }
                }

                AddBookingHistory(
                    reservation,
                    "BOOKING_TABLES_TRANSFERRED",
                    $"Chuyển từ [{oldTableText}] " +
                    $"sang [{newTableText}]. " +
                    $"Lý do: {model.Reason}");

                // Không return giữa hai lần lưu.
                // Nếu lần này lỗi, lần lưu phía trên cũng rollback.
                await _context.SaveChangesAsync(
                    cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã chuyển bàn từ {oldTableText} " +
                    $"sang {newTableText}.";

                return BackToDetails();
            },
            () => RedirectToRoute(
                "DoraFoodTransferBooking",
                new { id }),
            "BookingManagementError");
    }

    // Không chuyển lượt khách đã có hóa đơn
    // hoặc có đơn đã hoàn tất.
    private Task<bool> HasClosedTransferBillAsync(
        long reservationId)
    {
        return _context.Set<Order>()
            .AsNoTracking()
            .AnyAsync(
                order =>
                    order.ReservationId == reservationId
                    && (
                        order.Status == "COMPLETED"
                        || order.Invoice != null),
                HttpContext.RequestAborted);
    }

    // Đọc dữ liệu hiển thị và xác định bàn được phép chọn.
    private async Task LoadTransferFormAsync(
        TransferBookingViewModel model,
        Reservation reservation,
        Order? activeOrder)
    {
        var cancellationToken = HttpContext.RequestAborted;

        model.Id = reservation.Id;
        model.Reservation = reservation;
        model.ActiveOrderCode = activeOrder?.Code;

        var currentIds = reservation.Tables
            .Select(table => table.Id)
            .ToHashSet();

        model.CurrentTables = reservation.Tables
            .OrderBy(table => table.Area.Name)
            .ThenBy(table => table.Code)
            .Select(table => new TransferTableOption
            {
                Id = table.Id,
                AreaId = table.AreaId,
                AreaName = table.Area.Name,
                Code = table.Code,
                Capacity = table.Capacity,
                IsCurrent = true,
                IsAvailable =
                    table.IsActive && table.Area.IsActive
            })
            .ToList();

        if (!TryGetServiceSessionBounds(
                reservation.StartsAtUtc,
                out var sessionStartUtc,
                out var sessionEndUtc))
        {
            model.Tables = Array.Empty<TransferTableOption>();

            ModelState.AddModelError(
                string.Empty,
                "Giờ đặt bàn không thuộc buổi phục vụ hợp lệ.");

            return;
        }

        var now = DateTime.UtcNow;

        // Nếu khách dùng bữa sang buổi khác,
        // kiểm tra thêm lịch giữ bàn của buổi hiện tại.
        var hasCurrentSession = TryGetServiceSessionBounds(
            now,
            out var currentSessionStartUtc,
            out var currentSessionEndUtc);

        var reservedTableIds = await _context
            .Set<Reservation>()
            .AsNoTracking()
            .Where(other =>
                other.Id != reservation.Id
                && (
                    // Khách đang ngồi luôn chặn bàn.
                    other.Status == "SEATED"

                    // Các lượt còn hiệu lực trong buổi liên quan.
                    || (
                        (
                            other.Status == "PENDING"
                            || other.Status == "CONFIRMED")
                        && other.StartsAtUtc.AddMinutes(30) > now
                        && (
                            (
                                other.StartsAtUtc >= sessionStartUtc
                                && other.StartsAtUtc < sessionEndUtc)
                            || (
                                hasCurrentSession
                                && other.StartsAtUtc >= currentSessionStartUtc
                                && other.StartsAtUtc < currentSessionEndUtc)))
                ))
            .SelectMany(other => other.Tables)
            .Select(table => table.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        var excludedOrderId = activeOrder?.Id ?? 0L;

        var occupiedTableIds = await _context.Set<OrderTable>()
            .AsNoTracking()
            .Where(link =>
                link.ReleasedAtUtc == null
                && link.OrderId != excludedOrderId)
            .Select(link => link.TableId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var reservedIds = reservedTableIds.ToHashSet();
        var occupiedIds = occupiedTableIds.ToHashSet();

        var tables = await _context.Set<DiningTable>()
            .AsNoTracking()
            .Include(table => table.Area)
            .Where(table =>
                table.IsActive && table.Area.IsActive)
            .OrderBy(table => table.Area.Name)
            .ThenBy(table => table.Code)
            .ToListAsync(cancellationToken);

        model.Tables = tables.Select(table =>
        {
            var occupied = occupiedIds.Contains(table.Id);
            var reserved = reservedIds.Contains(table.Id);

            return new TransferTableOption
            {
                Id = table.Id,
                AreaId = table.AreaId,
                AreaName = table.Area.Name,
                Code = table.Code,
                Capacity = table.Capacity,
                IsCurrent = currentIds.Contains(table.Id),
                IsAvailable = !occupied && !reserved,
                UnavailableReason = occupied
                    ? "Bàn đang phục vụ đơn khác."
                    : reserved
                        ? "Bàn đang được giữ cho lượt khách khác."
                        : null
            };
        }).ToList();

        ViewData["Title"] =
            $"Chuyển bàn · {reservation.Code}";
    }

    // Giới hạn độ dài để lịch sử vẫn nằm trong cột Summary.
    private static string TransferTableNames(
        IEnumerable<string> codes)
    {
        var text = string.Join(
            ", ",
            codes.OrderBy(code => code));

        return text.Length <= 600
            ? text
            : text[..600] + "...";
    }
}
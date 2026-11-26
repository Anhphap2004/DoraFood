using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public sealed partial class StaffController
{
    [HttpGet("ban/{tableId:long}/dat-truoc/{bookingId:long}", Name = "DoraFoodStaffPreorderDetails")]
    public async Task<IActionResult> PreorderDetails(
        [FromRoute] long tableId, [FromRoute] long bookingId,
        [FromQuery] bool adminBoard, CancellationToken cancellationToken)
    {
        adminBoard = adminBoard && ViewData["CanCollectTablePayments"] is true;
        var boardRoute = adminBoard ? "DoraFoodAdminTableService" : "DoraFoodStaffDesk";
        var now = DateTime.UtcNow;
        var booking = await HeldReservationsForServiceDay(now)
            .Where(item => item.Id == bookingId
                && item.Tables.Any(table => table.Id == tableId && table.IsActive && table.Area.IsActive))
            .Include(item => item.Tables)
            .SingleOrDefaultAsync(cancellationToken);

        if (booking is null)
        {
            var activeOrderId = await OpenTableOrders()
                .Where(order => order.ReservationId == bookingId
                    && order.OrderTables.Any(link => link.TableId == tableId && link.ReleasedAtUtc == null))
                .Select(order => (long?)order.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeOrderId.HasValue)
            {
                TempData["StaffSuccess"] = "Lượt đặt này đã được nhận bàn.";
                TempData.Remove("BookingManagementError");
                return RedirectToRoute(TableOrderRoute, new { id = activeOrderId.Value });
            }

            TempData["StaffError"] = TempData["BookingManagementError"] as string
                ?? "Lượt đặt này không còn chờ nhận bàn.";
            return RedirectToRoute(boardRoute);
        }

        var orders = await _db.Set<Order>().AsNoTracking()
            .Where(order => order.ReservationId == booking.Id && order.OrderType == "DINE_IN"
                && (order.Status == "OPEN" || order.Status == "IN_PROGRESS")
                && order.ClosedAtUtc == null && order.Invoice == null)
            .Select(order => order.Id)
            .OrderBy(id => id)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (orders.Count != 1)
        {
            TempData["StaffError"] = "Không xác định được đơn đặt món của lượt đặt này.";
            return RedirectToRoute(boardRoute);
        }

        var orderId = orders[0];
        var dishes = await _db.Set<OrderItem>().AsNoTracking()
            .Where(line => line.OrderId == orderId && line.Status != "CANCELLED")
            .OrderBy(line => line.CreatedAtUtc).ThenBy(line => line.Id)
            .Select(line => new StaffPreorderDish(line.NameSnapshot, line.Quantity, line.Note, line.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var tableIds = booking.Tables.Select(table => table.Id).ToArray();
        var occupied = await _db.Set<OrderTable>().AsNoTracking()
            .AnyAsync(link => tableIds.Contains(link.TableId) && link.ReleasedAtUtc == null, cancellationToken)
            || await _db.Set<Reservation>().AsNoTracking()
            .AnyAsync(other => other.Id != booking.Id && other.Status == "SEATED"
                && other.Tables.Any(table => tableIds.Contains(table.Id)), cancellationToken);

        var tableCode = booking.Tables.Single(table => table.Id == tableId).Code;
        return View("PreorderDetails", new StaffPreorderDetails(
            booking.Id, booking.Code, tableId, tableCode,
            booking.Tables.Select(table => table.Code).OrderBy(code => code).ToList(),
            booking.StartsAtUtc, booking.PartySize, Convert.ToBase64String(booking.Version),
            adminBoard, occupied, dishes));
    }
}

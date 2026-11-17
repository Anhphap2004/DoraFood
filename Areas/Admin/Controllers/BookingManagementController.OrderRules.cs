using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

namespace DoraFood.Controllers;

public partial class BookingManagementController
{
    // Quyền thao tác món dành cho nhân viên quản lý.
    // Controller đã có kiểm tra đăng nhập và vai trò.
    private static bool CanStaffManageTableOrder(Order order)
    {
        if (order.OrderType != "DINE_IN"
            || (
                order.Status != "OPEN"
                && order.Status != "IN_PROGRESS")
            || order.ClosedAtUtc.HasValue
            || order.Invoice != null
            || order.Reservation == null)
        {
            return false;
        }

        var reservation = order.Reservation;

        // Món đặt trước: chưa được chiếm bàn.
        if (reservation.Status == "CONFIRMED")
        {
            return DateTime.UtcNow
                    < reservation.StartsAtUtc.AddMinutes(30)
                && !order.OrderTables.Any(
                    link => link.ReleasedAtUtc == null);
        }

        // Khách đã đến: phải có bàn đang phục vụ.
        return reservation.Status == "SEATED"
            && order.OrderTables.Any(
                link => link.ReleasedAtUtc == null);
    }

    private void NotifyCustomerAboutTableOrder(
        Order order,
        string title,
        string message)
    {
        var reservation = order.Reservation;

        if (reservation == null)
        {
            return;
        }

        _notifications.Add(
            reservation.CustomerId,
            title,
            $"Lượt đặt bàn {reservation.Code}. {message}",
            Url.RouteUrl(
                "DoraFoodMyBookingDetails",
                new
                {
                    area = "",
                    id = reservation.Id
                })
                ?? "/dat-ban/cua-toi");
    }
}
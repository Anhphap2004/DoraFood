using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace DoraFood.Controllers;

public partial class BookingManagementController
{
    private void AddBookingStatusNotification(
        Reservation reservation,
        string action)
    {
        if (!reservation.CustomerId.HasValue
            || reservation.CustomerId.Value <= 0)
        {
            return;
        }

        var arrivalText = reservation.StartsAtUtc
            .AddHours(7)
            .ToString("dd/MM/yyyy HH:mm");

        var tableText = string.Join(
            ", ",
            reservation.Tables
                .OrderBy(table => table.Code)
                .Select(table => table.Code));

        if (string.IsNullOrWhiteSpace(tableText))
        {
            tableText = "Chưa phân bàn";
        }
        else if (tableText.Length > 600)
        {
            tableText = tableText[..600] + "...";
        }

        (string Title, string Body)? message = action switch
        {
            "BOOKING_SEATED" => (
                "Nhà hàng đã nhận khách vào bàn",
                $"DoraFood đã nhận khách cho lượt đặt bàn " +
                $"{reservation.Code}. " +
                $"Bàn đang phục vụ: {tableText}. " +
                $"Số khách: {reservation.PartySize}. " +
                "Chúc quý khách có bữa ăn ngon miệng!"
            ),

            "BOOKING_TABLES_TRANSFERRED" => (
                "Đã cập nhật bàn đang phục vụ",
                $"Lượt đặt bàn {reservation.Code} " +
                $"đã được chuyển sang các bàn: {tableText}. " +
                "Đơn gọi món và các khoản tiền đã thu được giữ nguyên."
            ),

            "BOOKING_UPDATED" => (
                "Thông tin đặt bàn đã được điều chỉnh",
                $"Yêu cầu {reservation.Code} đã được cập nhật. " +
                $"Giờ đến: {arrivalText}. " +
                $"Số khách: {reservation.PartySize}. " +
                $"Bàn: {tableText}. " +
                "Quý khách vui lòng kiểm tra lại thông tin."
            ),

            "BOOKING_CANCELLED" => (
                "Yêu cầu đặt bàn đã được hủy",
                $"Yêu cầu {reservation.Code}, " +
                $"giờ đến {arrivalText}, đã được hủy. " +
                "Quý khách có thể liên hệ nhà hàng " +
                "nếu cần kiểm tra hoặc đặt lại."
            ),

            "BOOKING_NO_SHOW" => (
                "Cập nhật lượt đặt bàn",
                $"Nhà hàng ghi nhận khách chưa đến đối với " +
                $"yêu cầu {reservation.Code}, " +
                $"giờ hẹn {arrivalText}. " +
                "Bàn không còn được giữ cho yêu cầu này. " +
                "Quý khách vui lòng liên hệ nếu vẫn muốn đến."
            ),

            "BOOKING_COMPLETED" => (
                "Đã hoàn tất lượt phục vụ",
                $"Lượt đặt bàn {reservation.Code} đã hoàn tất. " +
                "DoraFood cảm ơn quý khách và hẹn gặp lại!"
            ),

            // BOOKING_CONFIRMED đã được gửi trực tiếp
            // trong action Confirm, không gửi lặp ở đây.
            _ => null
        };

        if (!message.HasValue)
        {
            return;
        }

        _notifications.Add(
            reservation.CustomerId,
            message.Value.Title,
            message.Value.Body,
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
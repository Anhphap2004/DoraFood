using System;
using System.Collections.Generic;
using DoraFood.Models;

namespace DoraFood.ViewModels;

public sealed class MyBookingDetailsViewModel
{
    public Reservation Reservation { get; init; } = null!;

    public IReadOnlyList<Order> Orders { get; init; }
        = Array.Empty<Order>();

    public string StatusLabel => Reservation.Status switch
    {
        "PENDING" => "Chờ nhà hàng xác nhận",
        "CONFIRMED" => "Đã xác nhận",
        "SEATED" => "Đang dùng bữa",
        "COMPLETED" => "Đã hoàn tất",
        "CANCELLED" => "Đã hủy",
        "NO_SHOW" => "Khách không đến",
        _ => "Chưa xác định"
    };

    public string StatusBadge => Reservation.Status switch
    {
        "PENDING" => "bg-warning text-dark",
        "CONFIRMED" => "bg-primary",
        "SEATED" => "bg-success",
        "COMPLETED" => "bg-secondary",
        "CANCELLED" => "bg-danger",
        "NO_SHOW" => "bg-dark",
        _ => "bg-secondary"
    };
}
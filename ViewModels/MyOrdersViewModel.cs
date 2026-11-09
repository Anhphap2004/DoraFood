using System;
using System.Collections.Generic;

namespace DoraFood.ViewModels;

public sealed record MyOrderRow(
    long Id,
    string Code,
    DateTime OpenedAtUtc,
    string Status,
    decimal Total)
{
    public string StatusLabel => Status switch
    {
        "OPEN" => "Chờ tiếp nhận",
        "IN_PROGRESS" => "Đang xử lý",
        "COMPLETED" => "Hoàn tất",
        "CANCELLED" => "Đã hủy",
        _ => "Chưa xác định"
    };

    public string StatusBadge => Status switch
    {
        "OPEN" => "bg-warning text-dark",
        "IN_PROGRESS" => "bg-primary text-white",
        "COMPLETED" => "bg-success text-white",
        "CANCELLED" => "bg-secondary text-white",
        _ => "bg-light text-dark"
    };
}

public sealed record MyOrdersViewModel(
    IReadOnlyList<MyOrderRow> Orders,
    int PageNumber,
    int TotalPages,
    int TotalOrders);

public sealed record MyOrderItemRow(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    string Status)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

public sealed record MyOrderDetailsViewModel(
    MyOrderRow Order,
    string? ReceiverName,
    string? PhoneNumber,
    string? DeliveryAddress,
    string? Note,
    IReadOnlyList<MyOrderItemRow> Items);
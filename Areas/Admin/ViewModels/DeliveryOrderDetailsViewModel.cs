using System;
using System.Collections.Generic;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record DeliveryOrderItemRow(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    string Status)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

public sealed record DeliveryOrderDetailsViewModel(
    string Code,
    string? GuestName,
    string? GuestPhone,
    string? DeliveryAddress,
    string? Note,
    DateTime OpenedAtUtc,
    string Status,
    decimal PendingAmount,
    decimal ReceivedAmount,
    IReadOnlyList<DeliveryOrderItemRow> Items,
    int ReturnPage);
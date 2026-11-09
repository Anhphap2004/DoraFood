using System;
using System.Collections.Generic;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record DeliveryOrderRow(
    string Code,
    string? GuestName,
    string? GuestPhone,
    string? DeliveryAddress,
    DateTime OpenedAtUtc,
    string Status,
    decimal PendingAmount,
    decimal ReceivedAmount);

public sealed record DeliveryOrderListViewModel(
    IReadOnlyList<DeliveryOrderRow> Orders,
    int Page,
    int TotalPages,
    int TotalOrders);
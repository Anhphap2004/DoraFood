using System;
using System.Collections.Generic;

namespace DoraFood.ViewModels;

public sealed record StaffTableRow(long Id, string Code, string AreaName,
    int Capacity, bool IsHeld, long? SeatedReservationId, long? OrderId, bool HasItems, DateTime? PreorderAtUtc = null)
{
    public string StatusClass => OrderId.HasValue
        ? HasItems ? "ordered" : "open"
        : IsHeld || PreorderAtUtc.HasValue ? "held" : "free";

    public string StatusLabel => OrderId.HasValue
        ? HasItems ? "Đã gọi món" : "Đã mở · Chưa gọi món"
        : SeatedReservationId.HasValue ? "Đã nhận khách"
        : PreorderAtUtc.HasValue ? "Đã đặt bàn có món"
        : IsHeld ? "Đang giữ bàn" : "Bàn trống";
}

public sealed record StaffOrderLine(long Id, string Name, decimal Quantity, decimal UnitPrice,
    string? Note, string Status, string? ImageUrl, DateTime? AddedAtUtc = null);

public sealed record StaffDishOption(long Id, string Name, decimal Price, string? ImageUrl);

public sealed record StaffOrderDetails(long Id, string Code,
    IReadOnlyList<string> Tables, IReadOnlyList<StaffOrderLine> Lines,
    IReadOnlyList<StaffDishOption> DishOptions, IReadOnlyList<StaffOperation> Operations,
    bool CanCloseEmpty);

public sealed record StaffOperation(string Actor, string Summary, DateTime AtUtc);

public sealed record CheckLine(string Name, decimal Quantity, string? Note);

public sealed record StaffCheck(string Code, IReadOnlyList<string> Tables,
    IReadOnlyList<CheckLine> Lines);

using System;
using System.Collections.Generic;

namespace DoraFood.ViewModels;

public sealed class MyOrderCancellationViewModel
{
    public long OrderId { get; set; }

    public string Code { get; set; } = string.Empty;

    public bool CanRequest { get; set; }

    public bool HasPendingRequest { get; set; }

    public List<MyOrderCancellationRow> Requests { get; set; } = new();
}

public sealed record MyOrderCancellationRow(
    string Status,
    string Reason,
    DateTime RequestedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewNote);
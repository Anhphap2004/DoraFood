using System;
using System.Collections.Generic;

namespace DoraFood.ViewModels;

public sealed record PublicVoucherViewModel(
    string Code,
    string Name,
    string DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscount,
    decimal MinimumOrder,
    int? PerCustomerLimit,
    DateTime EndsAtUtc,
    string? CoverUrl,
    int? UsageLimit,
    long UsedCount)
{
    public long? RemainingUses => UsageLimit.HasValue
        ? Math.Max(0L, UsageLimit.Value - UsedCount)
        : null;
}

public sealed record PublicVoucherPageViewModel(
    string Filter,
    IReadOnlyList<PublicVoucherViewModel> Vouchers);

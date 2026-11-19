using System;

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
    string? CoverUrl);

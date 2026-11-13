using System;
using System.Collections.Generic;

namespace DoraFood.ViewModels;

public sealed class MyVouchersViewModel
{
    public IReadOnlyList<MyVoucherRow> Vouchers { get; init; }
        = Array.Empty<MyVoucherRow>();

    public int PageNumber { get; init; } = 1;

    public int TotalPages { get; init; } = 1;

    public int TotalVouchers { get; init; }
}

public sealed class MyVoucherRow
{
    public string Code { get; init; } = "";

    public string Name { get; init; } = "";

    public string DiscountType { get; init; } = "";

    public decimal DiscountValue { get; init; }

    public decimal? MaxDiscount { get; init; }

    public decimal MinimumOrder { get; init; }

    public DateTime EndsAtUtc { get; init; }

    public string? ImageUrl { get; init; }

    public string? ImageAlt { get; init; }

    public int? UsageLimit { get; init; }

    public int? PerCustomerLimit { get; init; }

    public long TotalUsed { get; init; }

    public long UsedByCustomer { get; init; }

    // Lượt còn lại của toàn bộ chương trình.
    public long? GlobalRemaining => UsageLimit.HasValue
        ? Math.Max(0L, (long)UsageLimit.Value - TotalUsed)
        : null;

    // Lượt còn lại theo giới hạn riêng của khách.
    public long? CustomerRemaining => PerCustomerLimit.HasValue
        ? Math.Max(0L, (long)PerCustomerLimit.Value - UsedByCustomer)
        : null;

    // Số lượt khách có thể dùng theo cả hai giới hạn.
    // NULL nghĩa là không giới hạn lượt.
    public long? AvailableUses
    {
        get
        {
            if (!GlobalRemaining.HasValue)
            {
                return CustomerRemaining;
            }

            if (!CustomerRemaining.HasValue)
            {
                return GlobalRemaining;
            }

            return Math.Min(
                GlobalRemaining.Value,
                CustomerRemaining.Value);
        }
    }
}
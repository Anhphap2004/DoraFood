using System.Collections.Generic;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record DishListRow(
    long Id,
    string Name,
    string Slug,
    string CategoryName,
    int VariantCount,
    decimal? LowestAvailablePrice,
    bool IsActive,
    string? ImageUrl,
    bool HasOtherLinks)
{
    public bool CanDelete => VariantCount == 0 && !HasOtherLinks;
}

public sealed record DishCategoryFilterOption(long Id, string Name);

public sealed record DishListViewModel(
    IReadOnlyList<DishListRow> Dishes,
    IReadOnlyList<DishCategoryFilterOption> CategoryOptions,
    string Keyword,
    long? CategoryId,
    string Status,
    int Page,
    int TotalPages,
    int TotalDishes);
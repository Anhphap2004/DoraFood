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
    string? ImageUrl);
public sealed record DishListViewModel(
    IReadOnlyList<DishListRow> Dishes,
    int Page,
    int TotalPages,
    int TotalDishes);
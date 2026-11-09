using System.Collections.Generic;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record DishCategoryRow(
    long Id,
    string Name,
    string Slug,
    string? ParentName,
    int SortOrder,
    int DishCount,
    int ChildCount,
    bool IsActive)
{
    public bool CanDelete => DishCount == 0 && ChildCount == 0;
}

public sealed record DishCategoryIndexViewModel(
    IReadOnlyList<DishCategoryRow> Categories,
    string Keyword,
    string Status,
    int Page,
    int TotalPages,
    int TotalCategories);
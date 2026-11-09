namespace DoraFood.Areas.Admin.ViewModels;

public sealed record DishCategoryRow(
    long Id,
    string Name,
    string Slug,
    string? ParentName,
    int SortOrder,
    int DishCount,
    bool IsActive);
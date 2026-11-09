namespace DoraFood.Areas.Admin.ViewModels;

public sealed record NavigationItemRow(
     long Id,
    string MenuName,
    string MenuCode,
    string Label,
    string Url,
    string? ParentLabel,
    int SortOrder,
    bool IsVisible);
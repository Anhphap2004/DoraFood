using System.Collections.Generic;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record DishVariantAdminRow(
    long Id,
    string Sku,
    string Name,
    string SellingUnit,
    decimal Price,
    bool IsAvailable,
    string Version);

public sealed record DishVariantsViewModel(
    long DishId,
    string DishName,
    IReadOnlyList<DishVariantAdminRow> Variants);
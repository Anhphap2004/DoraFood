using System.Collections.Generic;

namespace DoraFood.ViewModels;

public class MenuPageViewModel
{
    public string? Search { get; set; }
    public long? CategoryId { get; set; }
    public string Sort { get; set; } = "featured";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public string SelectedCategoryName { get; set; } = "Tất cả món ăn";
    public List<MenuCategoryViewModel> Categories { get; set; } = new();
    public List<MenuDishViewModel> Dishes { get; set; } = new();
}

public class MenuCategoryViewModel
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class MenuDishViewModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AllergenNote { get; set; }
    public bool IsFeatured { get; set; }
    public string ImageUrl { get; set; } = "~/images/dish-placeholder.svg";
    public string? ImageAlt { get; set; }
    public List<MenuVariantViewModel> Variants { get; set; } = new();
}

public class MenuVariantViewModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SellingUnit { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

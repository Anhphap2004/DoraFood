using System.Collections.Generic;

namespace DoraFood.ViewModels;

public class MenuDishDetailsViewModel
{
    public long Id { get; set; }
    public long CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? DescriptionHtml { get; set; }
    public string? AllergenNote { get; set; }
    public bool IsFeatured { get; set; }
    public List<MenuVariantViewModel> Variants { get; set; } = new();
    public List<MenuDishImageViewModel> Images { get; set; } = new();
}

public class MenuDishImageViewModel
{
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
}

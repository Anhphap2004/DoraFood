using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace DoraFood.ViewModels;

public class CartItemInput
{
    public long VariantId { get; set; }

    [Range(1, 99, ErrorMessage = "Số lượng phải từ 1 đến 99.")]
    public int Quantity { get; set; }
}

public class CartViewModel
{
    public Guid Version { get; set; }

    public List<CartLineViewModel> Items { get; set; } = new();

    public int TotalQuantity => Items.Sum(item => item.Quantity);

    public decimal Subtotal => Items
        .Where(item => item.IsAvailable)
        .Sum(item => item.LineTotal ?? 0);

    public bool HasUnavailableItems => Items.Any(item => !item.IsAvailable);
}

public class CartLineViewModel
{
    public long VariantId { get; set; }
    public long DishId { get; set; }
    public string DishName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string SellingUnit { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? LineTotal => UnitPrice * Quantity;
    public bool IsAvailable { get; set; }
    public string ImageUrl { get; set; } = "~/assets/img/dish-placeholder.svg";
    public string? ImageAlt { get; set; }
}
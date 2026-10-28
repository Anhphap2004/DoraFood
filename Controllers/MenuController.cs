using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public class MenuController : Controller
{
    private readonly DoraFoodContext _context;

    public MenuController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet("/thuc-don", Name = "DoraFoodMenu")]
    [HttpGet("/menu.html")]
    [HttpGet("/Menu")]
    [HttpGet("/Menu/Index")]
    public async Task<IActionResult> Index(
        string? q, long? categoryId, string? sort, int page = 1,
        CancellationToken cancellationToken = default)
    {
        // Reject malformed numeric filters rather than silently changing their meaning.
        if (!ModelState.IsValid)
            return BadRequest("Tham số lọc hoặc số trang không hợp lệ.");

        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (q?.Length > 200)
            return BadRequest("Từ khóa tìm kiếm tối đa 200 ký tự.");

        sort = sort is "price-asc" or "price-desc" or "name" ? sort : "featured";
        const int pageSize = 12;

        var activeCategories = await _context.Set<DishCategory>()
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new MenuCategoryViewModel
            {
                Id = c.Id, ParentId = c.ParentId, Name = c.Name, SortOrder = c.SortOrder
            })
            .ToListAsync(cancellationToken);

        var byId = activeCategories.ToDictionary(c => c.Id);
        // Exclude children of inactive/missing parents, and malformed category cycles.
        bool HasActiveAncestors(MenuCategoryViewModel category)
        {
            var visited = new HashSet<long> { category.Id };
            var parentId = category.ParentId;
            while (parentId.HasValue)
            {
                if (!visited.Add(parentId.Value) || !byId.TryGetValue(parentId.Value, out var parent))
                    return false;
                parentId = parent.ParentId;
            }
            return true;
        }

        var categories = activeCategories.Where(HasActiveAncestors).ToList();
        var navigationCategories = new List<MenuCategoryViewModel>();
        void AppendCategories(long? parentId)
        {
            foreach (var category in categories.Where(c => c.ParentId == parentId))
            {
                navigationCategories.Add(category);
                AppendCategories(category.Id);
            }
        }
        AppendCategories(null);
        var allowedIds = categories.Select(c => c.Id).ToArray();

        if (categoryId.HasValue && !categories.Any(c => c.Id == categoryId.Value))
            return NotFound("Danh mục không tồn tại hoặc đã ngừng hiển thị.");

        var dishesQuery = _context.Set<Dish>().AsNoTracking()
            .Where(d => d.IsActive && allowedIds.Contains(d.CategoryId)
                && d.DishVariants.Any(v => v.IsAvailable));

        if (categoryId.HasValue)
        {
            // Selecting a parent includes all its active descendants.
            var selectedIds = new HashSet<long> { categoryId.Value };
            var pending = new Queue<long>();
            pending.Enqueue(categoryId.Value);
            while (pending.Count > 0)
            {
                var parentId = pending.Dequeue();
                foreach (var child in categories.Where(c => c.ParentId == parentId))
                    if (selectedIds.Add(child.Id)) pending.Enqueue(child.Id);
            }
            var filterIds = selectedIds.ToArray();
            dishesQuery = dishesQuery.Where(d => filterIds.Contains(d.CategoryId));
        }

        if (q != null)
        {
            var keyword = q;
            dishesQuery = dishesQuery.Where(d => d.Name.Contains(keyword)
                || (d.ShortDescription != null && d.ShortDescription.Contains(keyword)));
        }

        var totalItems = await dishesQuery.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var ordered = sort switch
        {
            "price-asc" => dishesQuery.OrderBy(d => d.DishVariants
                .Where(v => v.IsAvailable).Min(v => v.Price)).ThenBy(d => d.Id),
            "price-desc" => dishesQuery.OrderByDescending(d => d.DishVariants
                .Where(v => v.IsAvailable).Min(v => v.Price)).ThenBy(d => d.Id),
            "name" => dishesQuery.OrderBy(d => d.Name).ThenBy(d => d.Id),
            _ => dishesQuery.OrderByDescending(d => d.IsFeatured).ThenBy(d => d.Id)
        };

        var dishes = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new MenuDishViewModel
            {
                Id = d.Id, Name = d.Name, CategoryName = d.Category.Name,
                Description = d.ShortDescription, AllergenNote = d.AllergenNote,
                IsFeatured = d.IsFeatured
            }).ToListAsync(cancellationToken);

        if (dishes.Count > 0)
        {
            var dishIds = dishes.Select(d => d.Id).ToArray();
            // Read the image table directly: do not rely on Dish.DishImage being a collection.
            var images = await _context.Set<DishImage>().AsNoTracking()
                .Where(i => dishIds.Contains(i.DishId))
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => new { i.DishId, i.MediaFile.Url, i.MediaFile.AltText })
                .ToListAsync(cancellationToken);
            var firstImages = images.GroupBy(i => i.DishId).ToDictionary(g => g.Key, g => g.First());

            var variants = await _context.Set<DishVariant>().AsNoTracking()
                .Where(v => dishIds.Contains(v.DishId) && v.IsAvailable)
                .OrderBy(v => v.Price).ThenBy(v => v.Id)
                .Select(v => new
                {
                    v.DishId,
                    Variant = new MenuVariantViewModel
                    {
                        Id = v.Id, Name = v.Name, SellingUnit = v.SellingUnit, Price = v.Price
                    }
                }).ToListAsync(cancellationToken);
            var variantsByDish = variants.ToLookup(v => v.DishId, v => v.Variant);

            foreach (var dish in dishes)
            {
                if (firstImages.TryGetValue(dish.Id, out var image))
                {
                    dish.ImageUrl = NormalizeImageUrl(image.Url);
                    dish.ImageAlt = image.AltText;
                }
                dish.Variants = variantsByDish[dish.Id].ToList();
            }
        }

        // A concurrent availability edit must not produce a card with no sellable portion.
        dishes.RemoveAll(d => d.Variants.Count == 0);

        return View(new MenuPageViewModel
        {
            Search = q, CategoryId = categoryId, Sort = sort, Page = page,
            PageSize = pageSize, TotalItems = totalItems, TotalPages = totalPages,
            SelectedCategoryName = categories.FirstOrDefault(c => c.Id == categoryId)?.Name
                ?? "Tất cả món ăn",
            Categories = navigationCategories, Dishes = dishes
        });
    }

    private static string NormalizeImageUrl(string? value)
    {
        const string fallback = "~/images/dish-placeholder.svg";
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();
        if (value.StartsWith("~/", StringComparison.Ordinal) && !value.Contains('\\'))
            return value;
        if (value.StartsWith("/", StringComparison.Ordinal)
            && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\'))
            return value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            return uri.AbsoluteUri;
        return fallback;
    }
}

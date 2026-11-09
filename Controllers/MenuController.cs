using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Ganss.Xss;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Controllers;

public class MenuController : Controller
{
    private readonly DoraFoodContext _context;
    private readonly ILogger<MenuController> _logger;

    public MenuController(DoraFoodContext context, ILogger<MenuController> logger)
    {
        _context = context;
        _logger = logger;
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
                dish.ImageUrl = NormalizeImageUrl(null);
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

    [HttpGet("/thuc-don/{id:long}", Name = "DoraFoodDishDetails")]
    [HttpGet("/Menu/Details/{id:long}")]
    public async Task<IActionResult> Details(
        long id, int reviewPage = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest("Số trang đánh giá không hợp lệ.");
        if (id <= 0) return NotFound("Không tìm thấy món ăn.");

        var userId = await GetReviewUserIdAsync(cancellationToken);
        var model = await LoadDishDetailsAsync(id, reviewPage, userId, cancellationToken);
        if (model == null) return NotFound("Không tìm thấy món ăn.");
        return View(model);
    }

    [Authorize]
    [HttpPost("/thuc-don/{id:long}/danh-gia", Name = "DoraFoodSaveDishReview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveReview(
        long id, [Bind(Prefix = "ReviewInput")] DishReviewInput input,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0) return NotFound("Không tìm thấy món ăn.");
        var userId = await GetReviewUserIdAsync(cancellationToken);
        if (!userId.HasValue)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Challenge();
        }

        var model = await LoadDishDetailsAsync(id, 1, userId, cancellationToken);
        if (model == null) return NotFound("Không tìm thấy món ăn.");
        model.ReviewInput = input;
        if (!ModelState.IsValid) return View("Details", model);

        // Không nhận UserId, Status hoặc Id đánh giá từ trình duyệt.
        var review = await _context.Set<DishReview>().AsTracking().SingleOrDefaultAsync(
            r => r.DishId == id && r.UserId == userId.Value, cancellationToken);
        var isNew = review == null;
        if (review == null)
        {
            review = new DishReview
            {
                DishId = id, UserId = userId.Value, CreatedAtUtc = DateTime.UtcNow
            };
            _context.Set<DishReview>().Add(review);
        }

        review.Rating = input.Rating!.Value;
        review.Body = string.IsNullOrWhiteSpace(input.Body) ? null : input.Body.Trim();
        review.Status = "APPROVED"; // Hiển thị ngay, không qua bước duyệt.
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
        {
            // Hai tab gửi lần đầu cùng lúc: unique (DishId, UserId) ngăn trùng.
            TempData["DishReviewError"] =
                "Tài khoản đã có đánh giá cho món này. Vui lòng kiểm tra rồi sửa đánh giá hiện có.";
            return Redirect(Url.RouteUrl("DoraFoodDishDetails", new { id }) + "#dish-reviews");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Không lưu được đánh giá món {DishId} của tài khoản {UserId}", id, userId);
            ModelState.AddModelError(string.Empty,
                "Chưa lưu được đánh giá. Vui lòng thử lại.");
            return View("Details", model);
        }

        TempData["DishReviewSuccess"] = isNew
            ? "Đánh giá của bạn đã hiển thị." : "Đã cập nhật đánh giá của bạn.";
        return Redirect(Url.RouteUrl("DoraFoodDishDetails", new { id }) + "#dish-reviews");
    }

    private async Task<long?> GetReviewUserIdAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true
            || !long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || id <= 0
            || !Guid.TryParse(User.FindFirstValue("SecurityStamp"), out var stamp))
            return null;

        var active = await _context.Set<DoraFood.Models.User>().AsNoTracking()
            .AnyAsync(u => u.Id == id && u.IsActive && u.SecurityStamp == stamp, cancellationToken);
        return active ? id : null;
    }

    private async Task<MenuDishDetailsViewModel?> LoadDishDetailsAsync(
        long id, int reviewPage, long? userId, CancellationToken cancellationToken)
    {
        var dish = await _context.Set<Dish>().AsNoTracking()
            .Where(d => d.Id == id && d.IsActive)
            .Select(d => new MenuDishDetailsViewModel
            {
                Id = d.Id, CategoryId = d.CategoryId, Name = d.Name,
                CategoryName = d.Category.Name, ShortDescription = d.ShortDescription,
                DescriptionHtml = d.DescriptionHtml, AllergenNote = d.AllergenNote,
                IsFeatured = d.IsFeatured
            }).SingleOrDefaultAsync(cancellationToken);

        if (dish == null) return null;

        // Cùng quy tắc với Index: danh mục và toàn bộ danh mục cha phải hoạt động.
        var categories = await _context.Set<DishCategory>().AsNoTracking()
            .Where(c => c.IsActive).Select(c => new { c.Id, c.ParentId })
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        long? categoryId = dish.CategoryId;
        var visited = new HashSet<long>();
        while (categoryId.HasValue)
        {
            if (!visited.Add(categoryId.Value)
                || !categories.TryGetValue(categoryId.Value, out var category))
                return null;
            categoryId = category.ParentId;
        }

        dish.Variants = await _context.Set<DishVariant>().AsNoTracking()
            .Where(v => v.DishId == id && v.IsAvailable)
            .OrderBy(v => v.Price).ThenBy(v => v.Id)
            .Select(v => new MenuVariantViewModel
            {
                Id = v.Id, Name = v.Name, SellingUnit = v.SellingUnit, Price = v.Price
            }).ToListAsync(cancellationToken);

        dish.Images = await _context.Set<DishImage>().AsNoTracking()
            .Where(i => i.DishId == id)
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new MenuDishImageViewModel
            {
                Url = i.MediaFile.Url, AltText = i.MediaFile.AltText
            }).ToListAsync(cancellationToken);
        foreach (var image in dish.Images) image.Url = NormalizeImageUrl(image.Url);
        if (dish.Images.Count == 0)
            dish.Images.Add(new MenuDishImageViewModel { Url = NormalizeImageUrl(null) });

        // Chỉ cho phép định dạng văn bản trong mô tả; không nhận script/style/thuộc tính HTML.
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(new[]
        {
            "p", "br", "ul", "ol", "li", "strong", "em", "b", "i", "h2", "h3", "h4", "blockquote"
        });
        sanitizer.AllowedAttributes.Clear();
        dish.DescriptionHtml = sanitizer.Sanitize(dish.DescriptionHtml ?? string.Empty);

        await LoadDishReviewsAsync(dish, reviewPage, userId, cancellationToken);
        ViewData["Title"] = dish.Name;
        return dish;
    }

    private async Task LoadDishReviewsAsync(
        MenuDishDetailsViewModel model, int page, long? userId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 5;
        var query = _context.Set<DishReview>().AsNoTracking()
            .Where(r => r.DishId == model.Id && r.Status == "APPROVED" && r.User.IsActive);
        var summary = await query.GroupBy(r => r.DishId)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .SingleOrDefaultAsync(cancellationToken);
        model.ReviewCount = summary?.Count ?? 0;
        model.AverageRating = summary?.Average ?? 0;
        model.TotalReviewPages = (int)Math.Ceiling(model.ReviewCount / (double)pageSize);
        model.ReviewPage = Math.Clamp(page, 1, Math.Max(1, model.TotalReviewPages));
        model.Reviews = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .Skip((model.ReviewPage - 1) * pageSize).Take(pageSize)
            .Select(r => new DishReviewDisplayViewModel
            {
                UserName = r.User.UserName, Rating = r.Rating,
                Body = r.Body, CreatedAtUtc = r.CreatedAtUtc
            }).ToListAsync(cancellationToken);

        model.CanReview = userId.HasValue;
        if (!userId.HasValue) return;
        var ownReview = await _context.Set<DishReview>().AsNoTracking()
            .Where(r => r.DishId == model.Id && r.UserId == userId.Value)
            .Select(r => new DishReviewInput { Rating = r.Rating, Body = r.Body })
            .SingleOrDefaultAsync(cancellationToken);
        model.HasOwnReview = ownReview != null;
        model.ReviewInput = ownReview ?? new DishReviewInput();
    }

    private static string NormalizeImageUrl(string? value)
    {
        const string fallback = "~/assets/img/dish-placeholder.svg";
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

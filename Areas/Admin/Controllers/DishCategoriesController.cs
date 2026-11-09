using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Data;
namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
public class DishCategoriesController : Controller
{
    private readonly DoraFoodContext _context;

    public DishCategoriesController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
     string? keyword,
     string? status,
     int page = 1,
     CancellationToken cancellationToken = default)
    {
        const int pageSize = 20;

        var search = keyword?.Trim() ?? string.Empty;

        if (search.Length > 150)
        {
            return BadRequest("Từ khóa tìm kiếm tối đa 150 ký tự.");
        }

        var selectedStatus = status is "active" or "inactive" ? status : "all";
        var query = _context.Set<DishCategory>().AsNoTracking();

        if (search.Length > 0)
        {
            query = query.Where(category =>
                category.Name.Contains(search) || category.Slug.Contains(search));
        }

        if (selectedStatus == "active")
        {
            query = query.Where(category => category.IsActive);
        }
        else if (selectedStatus == "inactive")
        {
            query = query.Where(category => !category.IsActive);
        }

        var totalCategories = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(
            1, (int)Math.Ceiling(totalCategories / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var categories = await query
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(category => new DishCategoryRow(
                category.Id,
                category.Name,
                category.Slug,
                category.Parent == null ? null : category.Parent.Name,
                category.SortOrder,
                category.Dishes.Count,
                category.InverseParent.Count,
                category.IsActive))
            .ToListAsync(cancellationToken);

        return View(new DishCategoryIndexViewModel(
            categories, search, selectedStatus, page, totalPages, totalCategories));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
    [FromRoute] long id,
    [FromForm] string? keyword,
    [FromForm] string? status,
    [FromForm] int page = 1,
    CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest("Mã danh mục không hợp lệ.");
        }

        var categories = _context.Set<DishCategory>();

        try
        {
            var deletedRows = await categories
                .Where(category => category.Id == id
                    && !category.Dishes.Any()
                    && !category.InverseParent.Any())
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows == 1)
            {
                TempData["DishCategorySuccess"] = "Đã xóa danh mục.";
            }
            else
            {
                var existing = await categories.AsNoTracking()
                    .Where(category => category.Id == id)
                    .Select(category => new
                    {
                        DishCount = category.Dishes.Count,
                        ChildCount = category.InverseParent.Count
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                TempData["DishCategoryError"] = existing == null
                    ? "Danh mục không còn trong danh sách."
                    : $"Không thể xóa. Danh mục còn {existing.DishCount} món "
                      + $"và {existing.ChildCount} danh mục con.";
            }
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            TempData["DishCategoryError"] =
                "Danh mục đang được dữ liệu khác sử dụng. Anh tải lại để kiểm tra.";
        }

        return RedirectToAction(nameof(Index),
            new { area = "Admin", keyword, status, page });
    }
    [HttpGet]
    public async Task<IActionResult> Create(
    CancellationToken cancellationToken = default)
    {
        var input = new DishCategoryInput();
        await LoadParentOptionsAsync(input, cancellationToken);

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] DishCategoryInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await LoadParentOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var name = input.Name.Trim();
        var slug = string.IsNullOrWhiteSpace(input.Slug)
            ? GenerateSlug(name)
            : input.Slug.Trim();

        if (string.IsNullOrEmpty(slug))
        {
            ModelState.AddModelError(nameof(input.Slug),
                "Không tạo được slug từ tên này. Anh nhập slug thủ công nhé.");
        }

        var categories = _context.Set<DishCategory>();

        if (input.ParentId.HasValue)
        {
            var parentExists = await categories.AnyAsync(
                category => category.Id == input.ParentId.Value,
                cancellationToken);

            if (!parentExists)
            {
                ModelState.AddModelError(nameof(input.ParentId),
                    "Danh mục cha không còn tồn tại.");
            }
        }

        if (slug.Length > 0 && await categories.AnyAsync(
            category => category.Slug == slug, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.Slug),
                "Slug này đã được sử dụng. Anh nhập một slug khác.");
        }

        if (!ModelState.IsValid)
        {
            await LoadParentOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var category = new DishCategory
        {
            Name = name,
            Slug = slug,
            ParentId = input.ParentId,
            SortOrder = input.SortOrder,
            IsActive = input.IsActive
        };

        categories.Add(category);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql
            && (sql.Number == 2601 || sql.Number == 2627))
        {
            ModelState.AddModelError(nameof(input.Slug),
                "Slug này vừa được sử dụng. Anh nhập một slug khác.");

            await LoadParentOptionsAsync(input, cancellationToken);
            return View(input);
        }

        TempData["DishCategorySuccess"] = $"Đã tạo danh mục {name}.";

        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    private async Task LoadParentOptionsAsync(
    DishCategoryInput input,
    CancellationToken cancellationToken,
    long? editingId = null)
    {
        var categories = await _context.Set<DishCategory>()
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new
            {
                category.Id,
                category.ParentId,
                category.Name,
                category.IsActive
            })
            .ToListAsync(cancellationToken);

        var parentLinks = categories.ToDictionary(
            category => category.Id,
            category => category.ParentId);

        input.ParentOptions = categories
            .Where(category => !editingId.HasValue
                || IsValidParent(category.Id, editingId.Value, parentLinks))
            .Select(category => new SelectListItem
            {
                Value = category.Id.ToString(CultureInfo.InvariantCulture),
                Text = category.IsActive
                    ? category.Name
                    : category.Name + " (đã tắt)"
            })
            .ToList();
    }
    [HttpGet]
    public async Task<IActionResult> Edit(
[FromRoute] long id,
CancellationToken cancellationToken = default)
    {
        var input = await _context.Set<DishCategory>()
            .AsNoTracking()
            .Where(category => category.Id == id)
            .Select(category => new DishCategoryInput
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                ParentId = category.ParentId,
                SortOrder = category.SortOrder,
                IsActive = category.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (input == null)
        {
            return NotFound();
        }

        await LoadParentOptionsAsync(input, cancellationToken, id);
        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        [FromForm] DishCategoryInput input,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        input.Id = id;

        if (!ModelState.IsValid)
        {
            await LoadParentOptionsAsync(input, cancellationToken, id);
            return View(input);
        }

        try
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var categories = _context.Set<DishCategory>();
            var category = await categories.AsTracking()
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (category == null)
            {
                return NotFound();
            }

            var parentLinks = await categories.AsNoTracking()
                .Select(item => new { item.Id, item.ParentId })
                .ToDictionaryAsync(
                    item => item.Id, item => item.ParentId, cancellationToken);

            if (!IsValidParent(input.ParentId, id, parentLinks))
            {
                ModelState.AddModelError(nameof(input.ParentId),
                    "Danh mục cha không tồn tại hoặc sẽ tạo vòng lặp.");
            }

            var slug = string.IsNullOrWhiteSpace(input.Slug)
                ? category.Slug
                : input.Slug.Trim();

            if (await categories.AnyAsync(
                item => item.Id != id && item.Slug == slug,
                cancellationToken))
            {
                ModelState.AddModelError(nameof(input.Slug),
                    "Slug này đã được danh mục khác sử dụng.");
            }

            if (ModelState.IsValid)
            {
                category.Name = input.Name.Trim();
                category.Slug = slug;
                category.ParentId = input.ParentId;
                category.SortOrder = input.SortOrder;
                category.IsActive = input.IsActive;

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                TempData["DishCategorySuccess"] =
                    $"Đã cập nhật danh mục {category.Name}.";

                return RedirectToAction(nameof(Index), new { area = "Admin" });
            }
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql
            && (sql.Number is 2601 or 2627))
        {
            ModelState.AddModelError(nameof(input.Slug),
                "Slug này vừa được danh mục khác sử dụng.");
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql && sql.Number == 1205)
        {
            ModelState.AddModelError(string.Empty,
                "Danh mục đang được cập nhật đồng thời. Anh thử lưu lại.");
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            ModelState.AddModelError(string.Empty,
                "Danh mục đang được cập nhật đồng thời. Anh thử lưu lại.");
        }

        await LoadParentOptionsAsync(input, cancellationToken, id);
        return View(input);
    }

    private static bool IsValidParent(
        long? parentId,
        long categoryId,
        IReadOnlyDictionary<long, long?> parentLinks)
    {
        var visited = new HashSet<long> { categoryId };

        while (parentId is long currentId)
        {
            if (!visited.Add(currentId)
                || !parentLinks.TryGetValue(currentId, out parentId))
            {
                return false;
            }
        }

        return true;
    }
    private static string GenerateSlug(string name)
    {
        var normalized = name
            .ToLowerInvariant()
            .Replace('đ', 'd')
            .Normalize(NormalizationForm.FormD);

        var withoutAccents = new StringBuilder();

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            {
                withoutAccents.Append(character);
            }
        }

        return Regex.Replace(withoutAccents.ToString(), "[^a-z0-9]+", "-")
            .Trim('-');
    }
}
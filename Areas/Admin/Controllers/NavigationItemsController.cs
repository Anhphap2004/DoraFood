using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
public class NavigationItemsController : Controller
{
    private readonly DoraFoodContext _context;

    public NavigationItemsController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken = default)
    {
        var items = _context.Set<NavigationItem>().AsNoTracking();
        var menus = _context.Set<NavigationMenu>().AsNoTracking();

        var query =
            from item in items
            join menu in menus on item.MenuId equals menu.Id
            join parentItem in items
                on item.ParentId equals (long?)parentItem.Id into parents
            from parent in parents.DefaultIfEmpty()
            orderby menu.Name, menu.Id, item.SortOrder, item.Id
            select new NavigationItemRow(
                item.Id,
                menu.Name,
                menu.Code,
                item.Label,
                item.Url,
                parent == null ? null : parent.Label,
                item.SortOrder,
                item.IsVisible);

        var rows = await query.ToListAsync(cancellationToken);

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(
    [FromRoute] long id,
    CancellationToken cancellationToken = default)
    {
        var input = await _context.Set<NavigationItem>()
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new NavigationItemEditInput
            {
                Id = item.Id,
                Label = item.Label,
                Url = item.Url,
                SortOrder = item.SortOrder,
                IsVisible = item.IsVisible
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (input == null)
        {
            return NotFound();
        }

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        [FromForm] NavigationItemEditInput input,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.Set<NavigationItem>()
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (item == null)
        {
            return NotFound();
        }

        input.Id = item.Id;

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var url = input.Url.Trim();

        if (!IsAllowedNavigationUrl(url))
        {
            ModelState.AddModelError(nameof(input.Url),
                "Dùng đường dẫn như /thuc-don, ~/thuc-don, # hoặc URL http/https.");

            return View(input);
        }

        item.Label = input.Label.Trim();
        item.Url = url;
        item.SortOrder = input.SortOrder;
        item.IsVisible = input.IsVisible;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty,
                "Không lưu được vì mục điều hướng đã thay đổi hoặc không còn tồn tại.");

            return View(input);
        }

        TempData["NavigationSuccess"] = $"Đã cập nhật mục {item.Label}.";

        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }
    [HttpGet]
    public async Task<IActionResult> Create(
  CancellationToken cancellationToken = default)
    {
        var input = new NavigationItemCreateInput();
        await LoadCreateOptionsAsync(input, cancellationToken);

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] NavigationItemCreateInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await LoadCreateOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var menuId = input.MenuId.GetValueOrDefault();

        var menuExists = await _context.Set<NavigationMenu>()
            .AnyAsync(menu => menu.Id == menuId, cancellationToken);

        if (!menuExists)
        {
            ModelState.AddModelError(nameof(input.MenuId),
                "Nhóm menu đã chọn không còn tồn tại.");
        }

        if (input.ParentId is long parentId)
        {
            var parentIsValid = await _context.Set<NavigationItem>()
                .AnyAsync(item => item.Id == parentId
                    && item.MenuId == menuId
                    && item.ParentId == null,
                    cancellationToken);

            if (!parentIsValid)
            {
                ModelState.AddModelError(nameof(input.ParentId),
                    "Hãy chọn một mục cấp chính thuộc cùng nhóm menu.");
            }
        }

        var url = input.Item.Url.Trim();

        if (!IsAllowedNavigationUrl(url))
        {
            ModelState.AddModelError("Item.Url",
                "Dùng đường dẫn như /thuc-don, ~/thuc-don, # hoặc URL http/https.");
        }

        if (!ModelState.IsValid)
        {
            await LoadCreateOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var item = new NavigationItem
        {
            MenuId = menuId,
            ParentId = input.ParentId,
            Label = input.Item.Label.Trim(),
            Url = url,
            SortOrder = input.Item.SortOrder,
            IsVisible = input.Item.IsVisible
        };

        _context.Set<NavigationItem>().Add(item);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql && sql.Number == 547)
        {
            ModelState.AddModelError(string.Empty,
                "Không lưu được mục. Anh chọn lại nhóm menu hoặc mục cha.");

            await LoadCreateOptionsAsync(input, cancellationToken);
            return View(input);
        }

        TempData["NavigationSuccess"] = $"Đã tạo mục {item.Label}.";

        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    private async Task LoadCreateOptionsAsync(
        NavigationItemCreateInput input,
        CancellationToken cancellationToken)
    {
        var menus = await _context.Set<NavigationMenu>()
            .AsNoTracking()
            .OrderBy(menu => menu.Name)
            .ThenBy(menu => menu.Id)
            .Select(menu => new { menu.Id, menu.Name, menu.Code })
            .ToListAsync(cancellationToken);

        input.MenuOptions = menus.Select(menu => new SelectListItem
        {
            Value = menu.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Text = $"{menu.Name} ({menu.Code})"
        }).ToList();

        input.ParentOptions = await _context.Set<NavigationItem>()
            .AsNoTracking()
            .Where(item => item.ParentId == null)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new NavigationParentOption(
                item.Id,
                item.MenuId,
                item.IsVisible ? item.Label : item.Label + " (đã ẩn)"))
            .ToListAsync(cancellationToken);
    }
    private bool IsAllowedNavigationUrl(string url)
    {
        if (url.Length == 0 || url.Any(char.IsControl) || url.Contains('\\'))
        {
            return false;
        }

        if (Url.IsLocalUrl(url) || url.StartsWith("#", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var externalUrl)
            && externalUrl.IsWellFormedOriginalString()
            && !string.IsNullOrEmpty(externalUrl.Host)
            && (externalUrl.Scheme == Uri.UriSchemeHttp
                || externalUrl.Scheme == Uri.UriSchemeHttps);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
    [FromRoute] long id,
    CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest("Mã mục điều hướng không hợp lệ.");
        }

        var items = _context.Set<NavigationItem>();

        try
        {
            var deletedRows = await items
                .Where(item => item.Id == id
                    && !items.Any(child => child.ParentId == item.Id))
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows == 1)
            {
                TempData["NavigationSuccess"] = "Đã xóa mục điều hướng.";
            }
            else if (await items.AnyAsync(item => item.Id == id, cancellationToken))
            {
                TempData["NavigationError"] =
                    "Mục này còn mục con. Anh xử lý các mục con trước khi xóa.";
            }
            else
            {
                TempData["NavigationError"] =
                    "Mục này không còn trong danh sách.";
            }
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            TempData["NavigationError"] =
                "Mục này đang được liên kết với mục khác. Anh tải lại danh sách để kiểm tra.";
        }

        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }
}
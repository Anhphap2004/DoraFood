using System;
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
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
public class DishesController : Controller
{
    private readonly DoraFoodContext _context;

    public DishesController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
    string? keyword,
    long? categoryId,
    string? status,
    int page = 1,
    CancellationToken cancellationToken = default)
    {
        const int pageSize = 20;
        var search = keyword?.Trim() ?? string.Empty;

        if (search.Length > 200)
        {
            return BadRequest("Từ khóa tối đa 200 ký tự.");
        }

        var selectedStatus = status is "active" or "inactive" ? status : "all";

        var categoryOptions = await _context.Set<DishCategory>()
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new DishCategoryFilterOption(
                category.Id, category.Name))
            .ToListAsync(cancellationToken);

        if (categoryId.HasValue
            && !categoryOptions.Any(category => category.Id == categoryId.Value))
        {
            categoryId = null;
        }

        var query = _context.Set<Dish>().AsNoTracking();

        if (search.Length > 0)
        {
            query = query.Where(dish =>
                dish.Name.Contains(search) || dish.Slug.Contains(search));
        }

        if (categoryId is long selectedCategoryId)
        {
            query = query.Where(dish => dish.CategoryId == selectedCategoryId);
        }

        if (selectedStatus == "active")
        {
            query = query.Where(dish => dish.IsActive);
        }
        else if (selectedStatus == "inactive")
        {
            query = query.Where(dish => !dish.IsActive);
        }

        var totalDishes = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(
            1, (int)Math.Ceiling(totalDishes / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var images = _context.Set<DishImage>().AsNoTracking();

        var dishes = await query
            .OrderBy(dish => dish.Name)
            .ThenBy(dish => dish.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(dish => new DishListRow(
                dish.Id,
                dish.Name,
                dish.Slug,
                dish.Category.Name,
                dish.DishVariants.Count,
                dish.DishVariants
                    .Where(variant => variant.IsAvailable)
                    .Select(variant => (decimal?)variant.Price)
                    .Min(),
                dish.IsActive,
                images
                    .Where(image => image.DishId == dish.Id)
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.Id)
                    .Select(image => image.MediaFile.Url)
                    .FirstOrDefault(),
                images.Any(image => image.DishId == dish.Id)
                    || dish.DishReviews.Any()
                    || dish.AddOns.Any()))
            .ToListAsync(cancellationToken);

        return View(new DishListViewModel(
            dishes, categoryOptions, search, categoryId,
            selectedStatus, page, totalPages, totalDishes));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
    [FromRoute] long id,
    [FromForm] string? keyword,
    [FromForm] long? categoryId,
    [FromForm] string? status,
    [FromForm] int page = 1,
    CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest("Mã món không hợp lệ.");
        }

        var dishes = _context.Set<Dish>();
        var images = _context.Set<DishImage>();

        try
        {
            var deletedRows = await dishes
                .Where(dish => dish.Id == id
                    && !dish.DishVariants.Any()
                    && !images.Any(image => image.DishId == dish.Id)
                    && !dish.DishReviews.Any()
                    && !dish.AddOns.Any())
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows == 1)
            {
                TempData["DishSuccess"] = "Đã xóa món ăn.";
            }
            else if (await dishes.AnyAsync(dish => dish.Id == id, cancellationToken))
            {
                TempData["DishError"] =
                    "Món còn khẩu phần, ảnh, đánh giá hoặc món thêm. "
                    + "Nếu muốn ẩn món, anh vào Sửa và bỏ chọn Bật món.";
            }
            else
            {
                TempData["DishError"] = "Món không còn trong danh sách.";
            }
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            TempData["DishError"] =
                "Món đang được dữ liệu khác sử dụng. Anh tải lại danh sách để kiểm tra.";
        }

        return RedirectToAction(nameof(Index),
            new { area = "Admin", keyword, categoryId, status, page });
    }
    [HttpGet]
    public async Task<IActionResult> Create(
CancellationToken cancellationToken = default)
    {
        var input = new DishInput();
        await LoadCategoryOptionsAsync(input, cancellationToken);

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] DishInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }

        if (input.CategoryId is not long categoryId || categoryId <= 0)
        {
            ModelState.AddModelError(nameof(input.CategoryId),
                "Anh chọn một danh mục món ăn hợp lệ.");

            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var categoryExists = await _context.Set<DishCategory>()
            .AnyAsync(category => category.Id == categoryId, cancellationToken);

        if (!categoryExists)
        {
            ModelState.AddModelError(nameof(input.CategoryId),
                "Danh mục đã chọn không còn tồn tại.");
        }

        var slug = input.Slug.Trim();
        var dishes = _context.Set<Dish>();

        if (await dishes.AnyAsync(dish => dish.Slug == slug, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.Slug),
                "Slug này đã được món khác sử dụng.");
        }

        if (!ModelState.IsValid)
        {
            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var dish = new Dish
        {
            CategoryId = categoryId,
            Name = input.Name.Trim(),
            Slug = slug,
            ShortDescription = string.IsNullOrWhiteSpace(input.ShortDescription)
                ? null
                : input.ShortDescription.Trim(),
            AllergenNote = string.IsNullOrWhiteSpace(input.AllergenNote)
                ? null
                : input.AllergenNote.Trim(),
            IsFeatured = input.IsFeatured,
            IsActive = input.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        dishes.Add(dish);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql
            && (sql.Number is 2601 or 2627))
        {
            ModelState.AddModelError(nameof(input.Slug),
                "Slug này vừa được món khác sử dụng.");

            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql && sql.Number == 547)
        {
            ModelState.AddModelError(nameof(input.CategoryId),
                "Không lưu được món vào danh mục đã chọn. Anh kiểm tra lại danh mục.");

            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }

        TempData["DishSuccess"] = $"Đã tạo món {dish.Name}.";

        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    private async Task LoadCategoryOptionsAsync(
        DishInput input,
        CancellationToken cancellationToken)
    {
        var categories = await _context.Set<DishCategory>()
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new
            {
                category.Id,
                category.Name,
                category.IsActive
            })
            .ToListAsync(cancellationToken);

        input.CategoryOptions = categories.Select(category => new SelectListItem
        {
            Value = category.Id.ToString(CultureInfo.InvariantCulture),
            Text = category.IsActive
                ? category.Name
                : category.Name + " (đã tắt)"
        }).ToList();
    }
    [HttpGet]
    public async Task<IActionResult> AddVariant(
[FromRoute] long id,
CancellationToken cancellationToken = default)
    {
        var input = new DishVariantInput();

        if (!await LoadVariantDishAsync(id, input, cancellationToken))
        {
            return NotFound();
        }

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddVariant(
        [FromRoute] long id,
        [FromForm] DishVariantInput input,
        CancellationToken cancellationToken = default)
    {
        if (!await LoadVariantDishAsync(id, input, cancellationToken))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var sku = input.Sku.Trim().ToUpperInvariant();
        var variants = _context.Set<DishVariant>();

        if (await variants.AnyAsync(variant => variant.Sku == sku, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.Sku),
                "Mã này đã được khẩu phần khác sử dụng.");

            return View(input);
        }

        variants.Add(new DishVariant
        {
            DishId = id,
            Sku = sku,
            Name = input.Name.Trim(),
            SellingUnit = input.SellingUnit.Trim(),
            Price = input.Price!.Value,
            IsAvailable = input.IsAvailable
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql
            && (sql.Number is 2601 or 2627))
        {
            ModelState.AddModelError(nameof(input.Sku),
                "Mã này vừa được khẩu phần khác sử dụng.");

            return View(input);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql && sql.Number == 547)
        {
            ModelState.AddModelError(string.Empty,
                "Không lưu được khẩu phần cho món này. Anh mở lại danh sách món để kiểm tra.");

            return View(input);
        }

        TempData["DishSuccess"] = $"Đã thêm khẩu phần cho món {input.DishName}.";

        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    private async Task<bool> LoadVariantDishAsync(
        long id,
        DishVariantInput input,
        CancellationToken cancellationToken)
    {
        var dishName = await _context.Set<Dish>()
            .AsNoTracking()
            .Where(dish => dish.Id == id)
            .Select(dish => dish.Name)
            .SingleOrDefaultAsync(cancellationToken);

        if (dishName == null)
        {
            return false;
        }

        input.DishId = id;
        input.DishName = dishName;

        return true;
    }
    private const long MaxDishImageBytes = 5L * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> UploadImage(
        [FromRoute] long id,
        CancellationToken cancellationToken = default)
    {
        var input = new DishImageInput();

        if (!await LoadDishImageFormAsync(id, input, cancellationToken))
        {
            return NotFound();
        }

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6L * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(
        [FromRoute] long id,
        [FromForm] DishImageInput input,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (!await LoadDishImageFormAsync(id, input, cancellationToken))
        {
            return NotFound();
        }

        if (!ModelState.IsValid || input.Image == null)
        {
            return View(input);
        }

        var file = input.Image;

        if (file.Length == 0 || file.Length > MaxDishImageBytes)
        {
            ModelState.AddModelError(nameof(input.Image),
                "Ảnh phải có dung lượng lớn hơn 0 và không quá 5 MB.");

            return View(input);
        }

        var format = await DetectDishImageFormatAsync(file, cancellationToken);

        if (format == null)
        {
            ModelState.AddModelError(nameof(input.Image),
                "Anh chọn file ảnh JPG hoặc PNG.");

            return View(input);
        }

        // Cập nhật liên kết ảnh chính hiện có.
        // Nếu món chỉ có ảnh chưa đánh dấu chính, dùng liên kết đầu tiên.
        var imageLink = await _context.Set<DishImage>()
            .AsTracking()
            .Where(image => image.DishId == id)
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var folder = Path.Combine(environment.WebRootPath, "uploads", "dishes");
        var fileName = Guid.NewGuid().ToString("N") + format.Value.Extension;
        var filePath = Path.Combine(folder, fileName);

        try
        {
            Directory.CreateDirectory(folder);

            await using var output = new FileStream(
                filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            await file.CopyToAsync(output, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteDishUpload(filePath);

            ModelState.AddModelError(nameof(input.Image),
                "Không ghi được ảnh vào thư mục tải lên. Anh kiểm tra quyền ghi thư mục.");

            return View(input);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDishUpload(filePath);
            throw;
        }

        var originalName = Path.GetFileName(file.FileName.Replace('\\', '/'));

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? uploadedBy = long.TryParse(userIdClaim, out var userId) && userId > 0
            ? userId
            : null;

        var media = new MediaFile
        {
            Url = "~/uploads/dishes/" + fileName,
            OriginalName = originalName.Length <= 255
                ? originalName
                : originalName[..255],
            MimeType = format.Value.MimeType,
            SizeBytes = file.Length,
            AltText = input.DishName,
            UploadedBy = uploadedBy,
            CreatedAtUtc = DateTime.UtcNow
        };

        if (imageLink == null)
        {
            imageLink = new DishImage
            {
                DishId = id,
                SortOrder = 0
            };

            _context.Set<DishImage>().Add(imageLink);
        }

        imageLink.MediaFile = media;
        imageLink.IsPrimary = true;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql
            && (sql.Number is 547 or 2601 or 2627))
        {
            TryDeleteDishUpload(filePath);

            ModelState.AddModelError(string.Empty,
                "Không lưu được liên kết ảnh. Anh tải lại trang rồi thử lại.");

            return View(input);
        }

        TempData["DishSuccess"] = $"Đã cập nhật ảnh chính cho món {input.DishName}.";

        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    private async Task<bool> LoadDishImageFormAsync(
        long id,
        DishImageInput input,
        CancellationToken cancellationToken)
    {
        var images = _context.Set<DishImage>().AsNoTracking();

        var dish = await _context.Set<Dish>()
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.Name,
                ImageUrl = images
                    .Where(image => image.DishId == item.Id)
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.Id)
                    .Select(image => image.MediaFile.Url)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (dish == null)
        {
            return false;
        }

        input.DishId = dish.Id;
        input.DishName = dish.Name;
        input.CurrentImageUrl = dish.ImageUrl;

        return true;
    }

    private static async Task<(string Extension, string MimeType)?>
        DetectDishImageFormatAsync(
            IFormFile file,
            CancellationToken cancellationToken)
    {
        var header = new byte[8];

        await using var stream = file.OpenReadStream();

        var bytesRead = 0;

        while (bytesRead < header.Length)
        {
            var count = await stream.ReadAsync(
                header,
                bytesRead,
                header.Length - bytesRead,
                cancellationToken);

            if (count == 0)
            {
                return null;
            }

            bytesRead += count;
        }

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return (".jpg", "image/jpeg");
        }

        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        if (header.SequenceEqual(pngSignature))
        {
            return (".png", "image/png");
        }

        return null;
    }
    [HttpGet]
    public async Task<IActionResult> Edit(
[FromRoute] long id,
CancellationToken cancellationToken = default)
    {
        var input = await _context.Set<Dish>()
            .AsNoTracking()
            .Where(dish => dish.Id == id)
            .Select(dish => new DishInput
            {
                Id = dish.Id,
                Name = dish.Name,
                Slug = dish.Slug,
                CategoryId = dish.CategoryId,
                ShortDescription = dish.ShortDescription,
                AllergenNote = dish.AllergenNote,
                IsFeatured = dish.IsFeatured,
                IsActive = dish.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (input == null)
        {
            return NotFound();
        }

        await LoadCategoryOptionsAsync(input, cancellationToken);
        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        [FromForm] DishInput input,
        CancellationToken cancellationToken = default)
    {
        var dishes = _context.Set<Dish>();

        var dish = await dishes.AsTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (dish == null)
        {
            return NotFound();
        }

        input.Id = dish.Id;

        if (!ModelState.IsValid)
        {
            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var categoryId = input.CategoryId.GetValueOrDefault();
        var categoryExists = await _context.Set<DishCategory>()
            .AnyAsync(category => category.Id == categoryId, cancellationToken);

        if (!categoryExists)
        {
            ModelState.AddModelError(nameof(input.CategoryId),
                "Danh mục đã chọn không còn tồn tại.");
        }

        var slug = input.Slug.Trim();

        if (await dishes.AnyAsync(
            item => item.Id != dish.Id && item.Slug == slug,
            cancellationToken))
        {
            ModelState.AddModelError(nameof(input.Slug),
                "Slug này đã được món khác sử dụng.");
        }

        if (!ModelState.IsValid)
        {
            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }

        dish.Name = input.Name.Trim();
        dish.Slug = slug;
        dish.CategoryId = categoryId;
        dish.ShortDescription = string.IsNullOrWhiteSpace(input.ShortDescription)
            ? null
            : input.ShortDescription.Trim();
        dish.AllergenNote = string.IsNullOrWhiteSpace(input.AllergenNote)
            ? null
            : input.AllergenNote.Trim();
        dish.IsFeatured = input.IsFeatured;
        dish.IsActive = input.IsActive;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotFound("Món ăn không còn tồn tại.");
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql
            && (sql.Number is 2601 or 2627))
        {
            ModelState.AddModelError(nameof(input.Slug),
                "Slug này vừa được món khác sử dụng.");

            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql && sql.Number == 547)
        {
            ModelState.AddModelError(nameof(input.CategoryId),
                "Không lưu được vào danh mục đã chọn. Anh chọn lại danh mục.");

            await LoadCategoryOptionsAsync(input, cancellationToken);
            return View(input);
        }

        TempData["DishSuccess"] = $"Đã cập nhật món {dish.Name}.";

        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    [HttpGet]
    public async Task<IActionResult> Variants(
    [FromRoute] long id,
    CancellationToken cancellationToken = default)
    {
        var dishName = await _context.Set<Dish>()
            .AsNoTracking()
            .Where(dish => dish.Id == id)
            .Select(dish => dish.Name)
            .SingleOrDefaultAsync(cancellationToken);

        if (dishName == null)
        {
            return NotFound();
        }

        var records = await _context.Set<DishVariant>()
            .AsNoTracking()
            .Where(variant => variant.DishId == id)
            .OrderBy(variant => variant.Name)
            .ThenBy(variant => variant.Id)
            .Select(variant => new
            {
                variant.Id,
                variant.Sku,
                variant.Name,
                variant.SellingUnit,
                variant.Price,
                variant.IsAvailable,
                variant.Version,
                HasOrders = variant.OrderItems.Any(),
                HasRecipe = variant.Recipe != null
            })
            .ToListAsync(cancellationToken);

        var variants = records.Select(variant => new DishVariantAdminRow(
     variant.Id,
     variant.Sku,
     variant.Name,
     variant.SellingUnit,
     variant.Price,
     variant.IsAvailable,
     Convert.ToBase64String(variant.Version),
     variant.HasOrders,
     variant.HasRecipe))
     .ToList();

        return View(new DishVariantsViewModel(id, dishName, variants));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVariant(
[FromRoute] long id,
[FromForm] long variantId,
[FromForm] string? version,
CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id <= 0 || variantId <= 0)
        {
            return BadRequest("Thông tin khẩu phần không hợp lệ.");
        }

        IActionResult BackWithError(string message)
        {
            TempData["VariantError"] = message;
            return RedirectToAction(nameof(Variants), new { area = "Admin", id });
        }

        var expectedVersion = new byte[8];

        if (version == null || version.Length != 12
            || !Convert.TryFromBase64String(version, expectedVersion, out var bytesWritten)
            || bytesWritten != expectedVersion.Length)
        {
            return BackWithError("Phiên chỉnh sửa không hợp lệ. Anh tải lại trang.");
        }

        var variants = _context.Set<DishVariant>();

        var result = await variants.AsTracking()
            .Where(variant => variant.Id == variantId && variant.DishId == id)
            .Select(variant => new
            {
                Variant = variant,
                HasOrders = variant.OrderItems.Any(),
                HasRecipe = variant.Recipe != null
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (result == null)
        {
            return BackWithError("Khẩu phần không còn trong danh sách của món này.");
        }

        if (!result.Variant.Version.SequenceEqual(expectedVersion))
        {
            return BackWithError(
                "Khẩu phần đã được cập nhật. Anh kiểm tra lại trước khi xóa.");
        }

        if (result.HasOrders || result.HasRecipe)
        {
            return BackWithError(
                "Khẩu phần đã có trong đơn hàng hoặc gắn công thức. "
                + "Anh có thể chuyển sang Tạm ngừng bán.");
        }

        variants.Remove(result.Variant);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BackWithError(
                "Khẩu phần vừa được thay đổi hoặc xóa. Anh kiểm tra lại danh sách.");
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql && sql.Number == 547)
        {
            return BackWithError(
                "Khẩu phần vừa được dữ liệu khác sử dụng nên không thể xóa.");
        }

        TempData["VariantSuccess"] = $"Đã xóa khẩu phần {result.Variant.Name}.";

        return RedirectToAction(nameof(Variants), new { area = "Admin", id });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVariant(
        [FromRoute] long id,
        [FromForm] long variantId,
        [FromForm] string? price,
        [FromForm] bool? isAvailable,
        [FromForm] string? version,
        CancellationToken cancellationToken = default)
    {
        IActionResult BackWithError(string message)
        {
            TempData["VariantError"] = message;
            return RedirectToAction(nameof(Variants), new { area = "Admin", id });
        }

        if (!ModelState.IsValid || id <= 0 || variantId <= 0 || !isAvailable.HasValue)
        {
            return BadRequest("Thông tin khẩu phần không hợp lệ.");
        }

        var expectedVersion = new byte[8];

        if (version == null || version.Length != 12
            || !Convert.TryFromBase64String(version, expectedVersion, out var bytesWritten)
            || bytesWritten != expectedVersion.Length)
        {
            return BackWithError("Phiên chỉnh sửa không hợp lệ. Anh mở lại trang rồi thử lại.");
        }

        var variant = await _context.Set<DishVariant>()
            .AsTracking()
            .SingleOrDefaultAsync(
                item => item.Id == variantId && item.DishId == id,
                cancellationToken);

        if (variant == null)
        {
            return NotFound();
        }

        if (!variant.Version.SequenceEqual(expectedVersion))
        {
            return BackWithError(
                "Khẩu phần đã được cập nhật. Anh xem giá và trạng thái mới trước khi sửa lại.");
        }

        if (!decimal.TryParse(
            price,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var newPrice))
        {
            return BackWithError("Giá không hợp lệ. Ví dụ, nhập 180000 cho 180.000 ₫.");
        }

        if (newPrice != variant.Price
            && (newPrice < 1
                || newPrice > 99999999999999999m
                || newPrice != decimal.Truncate(newPrice)))
        {
            return BackWithError("Giá mới phải là số nguyên dương tính bằng đồng.");
        }

        variant.Price = newPrice;
        variant.IsAvailable = isAvailable.Value;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BackWithError(
                "Khẩu phần vừa thay đổi. Anh xem lại dữ liệu mới rồi lưu lại.");
        }

        TempData["VariantSuccess"] = $"Đã cập nhật khẩu phần {variant.Name}.";

        return RedirectToAction(nameof(Variants), new { area = "Admin", id });
    }

    private static void TryDeleteDishUpload(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (IOException)
        {
            // Có thể dọn file thừa sau nếu thư mục đang bận.
        }
        catch (UnauthorizedAccessException)
        {
            // Không che thông báo lỗi lưu ảnh nếu thư mục thiếu quyền xóa.
        }
    }
}
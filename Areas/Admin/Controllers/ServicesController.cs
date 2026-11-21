using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[AutoValidateAntiforgeryToken]
[Route("admin/services")]
public sealed class ServicesController : AdminModuleController
{
    private const long MaxImageSize = 5 * 1024 * 1024;
    private const long MaxRequestSize = 6 * 1024 * 1024;
    private const decimal MaxBasePrice = 99999999999999999.99m;

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ServicesController> _logger;

    public ServicesController(
        DoraFoodContext db,
        IWebHostEnvironment environment,
        ILogger<ServicesController> logger)
        : base(db)
    {
        _environment = environment;
        _logger = logger;
    }

    // GET /admin/services
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var services = await Db.Services
            .AsNoTracking()
            .OrderByDescending(service => service.Id)
            .Select(service => new ServiceAdminRow(
                service.Id,
                service.Name,
                service.Slug,
                service.BasePrice,
                service.ImageUrl,
                service.IsActive))
            .ToListAsync(cancellationToken);

        return View(services);
    }

    // GET /admin/services/create
    [HttpGet("create")]
    public IActionResult Create()
    {
        return View("Form", new ServiceAdminInput());
    }

    // POST /admin/services/create
    [HttpPost("create")]
    [RequestSizeLimit(MaxRequestSize)]
    public Task<IActionResult> Create(
        [FromForm] ServiceAdminInput input,
        CancellationToken cancellationToken)
    {
        return SaveAsync(new Service(), input, cancellationToken);
    }

    // GET /admin/services/{id}/edit
    [HttpGet("{id:long}/edit")]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        var input = await Db.Services
            .AsNoTracking()
            .Where(service => service.Id == id)
            .Select(service => new ServiceAdminInput
            {
                Id = service.Id,
                Name = service.Name,
                Slug = service.Slug,
                DescriptionHtml = service.DescriptionHtml,
                BasePrice = service.BasePrice,
                ImageUrl = service.ImageUrl,
                IsActive = service.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        return input is null
            ? NotFound()
            : View("Form", input);
    }

    // POST /admin/services/{id}/edit
    [HttpPost("{id:long}/edit")]
    [RequestSizeLimit(MaxRequestSize)]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        [FromForm] ServiceAdminInput input,
        CancellationToken cancellationToken)
    {
        var service = await Db.Services
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (service is null)
        {
            return NotFound();
        }

        return await SaveAsync(service, input, cancellationToken);
    }

    // POST /admin/services/{id}/visibility
    [HttpPost("{id:long}/visibility")]
    public async Task<IActionResult> SetVisibility(
        [FromRoute] long id,
        [FromForm] bool? isActive,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !isActive.HasValue)
        {
            return BadRequest("Trạng thái hiển thị không hợp lệ.");
        }

        var service = await Db.Services
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (service is null)
        {
            return NotFound();
        }

        service.IsActive = isActive.Value;

        try
        {
            await Db.SaveChangesAsync(cancellationToken);

            TempData["ServiceSuccess"] = service.IsActive
                ? "Đã bật dịch vụ."
                : "Đã ẩn dịch vụ.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ServiceError"] =
                "Dịch vụ vừa thay đổi. Anh hãy tải lại danh sách.";
        }

        return RedirectToAction(nameof(Index));
    }

    // Luồng lưu dùng chung cho thêm mới và chỉnh sửa.
    private async Task<IActionResult> SaveAsync(
        Service service,
        ServiceAdminInput input,
        CancellationToken cancellationToken)
    {
        var isNew = service.Id == 0;

        // Giữ ID và ảnh hiện tại để hiển thị lại form nếu có lỗi.
        input.Id = service.Id;
        input.ImageUrl = service.ImageUrl;

        if (input.BasePrice is decimal price
            && (price < 0 || price > MaxBasePrice || decimal.Round(price, 2) != price))
        {
            ModelState.AddModelError(
                nameof(input.BasePrice),
                "Giá phải không âm, có tối đa 17 chữ số nguyên và 2 chữ số thập phân.");
        }

        if (!ModelState.IsValid)
        {
            return View("Form", input);
        }

        var slug = input.Slug;
        var slugExists = await Db.Services.AnyAsync(
            item => item.Slug == slug && item.Id != service.Id,
            cancellationToken);

        if (slugExists)
        {
            ModelState.AddModelError(nameof(input.Slug), "Slug này đã có.");
            return View("Form", input);
        }

        var description = string.IsNullOrWhiteSpace(input.DescriptionHtml)
            ? null
            : new HtmlSanitizer().Sanitize(input.DescriptionHtml.Trim());

        (string Url, string PhysicalPath)? uploadedImage = null;

        if (input.ImageFile is { } imageFile)
        {
            try
            {
                uploadedImage = await SaveImageAsync(imageFile, cancellationToken);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception,
                    "Không ghi được ảnh cho dịch vụ {ServiceId}.", service.Id);

                ModelState.AddModelError(
                    nameof(input.ImageFile),
                    "Không ghi được ảnh. Anh hãy thử lại.");

                return View("Form", input);
            }

            if (uploadedImage is null)
            {
                ModelState.AddModelError(
                    nameof(input.ImageFile),
                    "Chỉ nhận ảnh JPG, PNG hoặc WebP, tối đa 5 MB.");

                return View("Form", input);
            }
        }

        service.Name = input.Name.Trim();
        service.Slug = slug;
        service.BasePrice = input.BasePrice.GetValueOrDefault();
        service.DescriptionHtml = description;
        service.IsActive = input.IsActive;

        if (uploadedImage is { } newImage)
        {
            service.ImageUrl = newImage.Url;
        }

        if (isNew)
        {
            Db.Services.Add(service);
        }

        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            DeleteUploadIfPossible(uploadedImage?.PhysicalPath);

            return NotFound(
                "Dịch vụ không còn tồn tại. Anh hãy tải lại danh sách.");
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            DeleteUploadIfPossible(uploadedImage?.PhysicalPath);

            ModelState.AddModelError(
                nameof(input.Slug),
                "Slug này vừa được sử dụng. Anh hãy chọn slug khác.");

            return View("Form", input);
        }

        TempData["ServiceSuccess"] = isNew
            ? "Đã thêm dịch vụ."
            : "Đã cập nhật dịch vụ.";

        return RedirectToAction(nameof(Index));
    }

    // Kiểm tra dung lượng, nhận dạng định dạng và lưu ảnh.
    private async Task<(string Url, string PhysicalPath)?> SaveImageAsync(
        IFormFile image,
        CancellationToken cancellationToken)
    {
        if (image.Length < 12 || image.Length > MaxImageSize)
        {
            return null;
        }

        await using var input = image.OpenReadStream();
        var header = new byte[12];

        var bytesRead = await input.ReadAtLeastAsync(
    header.AsMemory(),
    header.Length,
    throwOnEndOfStream: false,
    cancellationToken: cancellationToken);

        if (bytesRead != header.Length)
        {
            return null;
        }

        var extension = GetImageExtension(header);
        if (extension is null)
        {
            return null;
        }

        var folder = Path.Combine(_environment.WebRootPath, "uploads", "services");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(folder, fileName);
        var temporaryPath = physicalPath + ".tmp";

        try
        {
            // Ghi xong vào tệp tạm rồi mới chuyển sang tên chính thức.
            await using (var output = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await output.WriteAsync(header.AsMemory(), cancellationToken);
                await input.CopyToAsync(output, cancellationToken);
            }

            System.IO.File.Move(temporaryPath, physicalPath);

            return ($"~/uploads/services/{fileName}", physicalPath);
        }
        finally
        {
            DeleteUploadIfPossible(temporaryPath);
        }
    }

    private static string? GetImageExtension(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        ReadOnlySpan<byte> jpeg = [0xFF, 0xD8, 0xFF];

        if (header.StartsWith(png))
        {
            return ".png";
        }

        if (header.StartsWith(jpeg))
        {
            return ".jpg";
        }

        if (header[..4].SequenceEqual("RIFF"u8)
            && header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }

    private void DeleteUploadIfPossible(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception,
                "Không dọn được tệp ảnh dịch vụ {FilePath}.", path);
        }
    }
}
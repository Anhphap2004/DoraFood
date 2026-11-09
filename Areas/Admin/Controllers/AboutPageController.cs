using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using ContentPage = DoraFood.Models.Page;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
public class AboutPageController : Controller
{
    // Đổi giá trị này nếu HomeController.About() dùng slug khác.
    private const string AboutSlug = "gioi-thieu";

    private readonly DoraFoodContext _context;

    public AboutPageController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Edit(
        CancellationToken cancellationToken = default)
    {
        var input = await _context.Set<ContentPage>()
            .AsNoTracking()
            .Where(page => page.Slug == AboutSlug)
            .Select(page => new AboutPageInput
            {
                Title = page.Title,
                ContentHtml = page.ContentHtml,
                MetaDescription = page.MetaDescription,
                IsPublished = page.IsPublished
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (input == null)
        {
            return NotFound($"Không tìm thấy trang có slug '{AboutSlug}'.");
        }

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromForm] AboutPageInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var page = await _context.Set<ContentPage>()
            .AsTracking()
            .SingleOrDefaultAsync(
                item => item.Slug == AboutSlug, cancellationToken);

        if (page == null)
        {
            return NotFound($"Không tìm thấy trang có slug '{AboutSlug}'.");
        }

        // Dùng HtmlSanitizer đã cài ở phần chi tiết món ăn.
        var sanitizer = new HtmlSanitizer();
        var sanitizedHtml = sanitizer.Sanitize(input.ContentHtml.Trim());

        if (string.IsNullOrWhiteSpace(sanitizedHtml))
        {
            ModelState.AddModelError(nameof(input.ContentHtml),
                "Nội dung trống sau khi lọc HTML. Anh kiểm tra lại nội dung.");

            return View(input);
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        page.Title = input.Title.Trim();
        page.ContentHtml = sanitizedHtml;
        page.MetaDescription = string.IsNullOrWhiteSpace(input.MetaDescription)
            ? null
            : input.MetaDescription.Trim();
        page.IsPublished = input.IsPublished;
        page.UpdatedAtUtc = DateTime.UtcNow;
        page.UpdatedBy = long.TryParse(userIdClaim, out var userId) && userId > 0
            ? userId
            : null;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty,
                "Không lưu được vì trang đã thay đổi hoặc không còn tồn tại.");

            return View(input);
        }

        TempData["AboutPageSuccess"] = "Đã cập nhật trang Giới thiệu.";

        return RedirectToAction(nameof(Edit), new { area = "Admin" });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(
    IFormFile? file,
    [FromServices] IWebHostEnvironment environment,
    CancellationToken cancellationToken)
    {
        const int maxFileSize = 5 * 1024 * 1024;

        if (file is null || file.Length < 8 || file.Length > maxFileSize)
        {
            return BadRequest(new { message = "Hãy chọn ảnh JPG hoặc PNG tối đa 5 MB." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        await using var input = file.OpenReadStream();

        var header = new byte[8];
        await input.ReadExactlyAsync(header.AsMemory(), cancellationToken);

        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        var isPng = extension == ".png"
            && header.AsSpan().SequenceEqual(pngSignature);

        var isJpeg = (extension is ".jpg" or ".jpeg")
            && header[0] == 0xFF
            && header[1] == 0xD8
            && header[2] == 0xFF;

        if (!isPng && !isJpeg)
        {
            return BadRequest(new { message = "File phải là ảnh JPG hoặc PNG hợp lệ." });
        }

        var folder = Path.Combine(environment.WebRootPath, "uploads", "about");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{(isPng ? ".png" : ".jpg")}";
        var filePath = Path.Combine(folder, fileName);
        var tempPath = filePath + ".tmp";

        try
        {
            await using (var output = new FileStream(tempPath, FileMode.CreateNew))
            {
                await output.WriteAsync(header.AsMemory(), cancellationToken);
                await input.CopyToAsync(output, cancellationToken);
            }

            System.IO.File.Move(tempPath, filePath);
        }
        finally
        {
            System.IO.File.Delete(tempPath);
        }

        return Ok(new { url = Url.Content($"~/uploads/about/{fileName}") });
    }
}
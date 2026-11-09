using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Helpers;
using DoraFood.Models;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[Route("admin/posts")]
public class PostsController : Controller
{
    private const int PageSize = 10;

    private readonly DoraFoodContext _context;

    public PostsController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken,
        string? keyword = null,
        long? categoryId = null,
        string? status = null,
        int pageNumber = 1)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Danh mục hoặc số trang không hợp lệ.");
        }

        keyword = keyword?.Trim();
        status = status?.Trim().ToUpperInvariant();

        if (keyword?.Length > 250)
        {
            return BadRequest("Từ khóa tối đa 250 ký tự.");
        }

        if (!string.IsNullOrEmpty(status)
            && status is not ("DRAFT" or "PUBLISHED" or "ARCHIVED"))
        {
            return BadRequest("Trạng thái bài viết không hợp lệ.");
        }

        var query = _context.Set<Post>().AsNoTracking();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(post =>
                post.Title.Contains(keyword) || post.Slug.Contains(keyword));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(post => post.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(post => post.Status == status);
        }

        var totalPosts = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalPosts / (double)PageSize));
        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var posts = await query
            .OrderByDescending(post => post.CreatedAtUtc)
            .ThenByDescending(post => post.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(post => new PostRow(
                post.Id,
                post.Title,
                post.Slug,
                post.Category.Name,
                post.Author.UserName,
                post.Cover == null ? null : post.Cover.Url,
                post.Status,
                post.PublishedAtUtc,
                post.Version))
            .ToListAsync(cancellationToken);

        var categories = await _context.Set<PostCategory>()
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new CategoryOption(
                category.Id,
                category.Name,
                category.IsActive))
            .ToListAsync(cancellationToken);

        return View(new IndexViewModel(
            posts,
            categories,
            keyword,
            categoryId,
            status,
            pageNumber,
            totalPages,
            totalPosts));
    }

    [HttpGet("create")]
    public Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return ShowFormAsync(nameof(Create), new PostFormInput(), cancellationToken);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        PostFormInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ShowFormAsync(nameof(Create), input, cancellationToken);
        }

        var authorClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!long.TryParse(authorClaim, out var authorId)
            || !await _context.Set<User>().AnyAsync(
                user => user.Id == authorId && user.IsActive,
                cancellationToken))
        {
            return Forbid();
        }

        if (input.CategoryId is not long categoryId
            || !await IsActiveCategoryAsync(categoryId, cancellationToken))
        {
            ModelState.AddModelError(
                nameof(input.CategoryId),
                "Anh hãy chọn một danh mục đang hoạt động.");

            return await ShowFormAsync(nameof(Create), input, cancellationToken);
        }

        var cleanHtml = ValidatePostContent(input.ContentHtml);

        if (!ModelState.IsValid)
        {
            return await ShowFormAsync(nameof(Create), input, cancellationToken);
        }

        var slug = await SlugHelper.ResolveAsync(
            input.Slug,
            input.Title,
            _context.Set<Post>().Select(post => post.Slug),
            cancellationToken);

        var post = new Post
        {
            Title = input.Title.Trim(),
            Slug = slug,
            CategoryId = categoryId,
            AuthorId = authorId,
            Summary = string.IsNullOrWhiteSpace(input.Summary)
                ? null
                : input.Summary.Trim(),
            ContentHtml = cleanHtml,
            Status = "DRAFT",
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Set<Post>().Add(post);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(
                nameof(input.Slug),
                "Slug này đã được dùng. Anh hãy nhập slug khác hoặc để trống để tự tạo.");

            return await ShowFormAsync(nameof(Create), input, cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 547 })
        {
            ModelState.AddModelError(
                string.Empty,
                "Dữ liệu tác giả hoặc danh mục vừa thay đổi. Anh hãy kiểm tra rồi thử lại.");

            return await ShowFormAsync(nameof(Create), input, cancellationToken);
        }

        TempData["PostSuccess"] = "Đã tạo bản nháp. Anh có thể tải ảnh bìa tại đây.";
        return RedirectToAction(nameof(Edit), new { id = post.Id });
    }

    [HttpGet("{id:long}/edit")]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        var post = await _context.Set<Post>()
            .AsNoTracking()
            .Include(item => item.Cover)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (post is null)
        {
            return NotFound();
        }

        var input = new PostFormInput
        {
            Id = post.Id,
            Title = post.Title,
            Slug = post.Slug,
            CategoryId = post.CategoryId,
            Summary = post.Summary,
            ContentHtml = post.ContentHtml,
            Version = Convert.ToBase64String(post.Version),
            CoverUrl = post.Cover?.Url
        };

        return await ShowFormAsync(
            nameof(Edit), input, cancellationToken, post.CategoryId);
    }

    [HttpPost("{id:long}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        PostFormInput input,
        CancellationToken cancellationToken)
    {
        var post = await _context.Set<Post>()
            .AsTracking()
            .Include(item => item.Cover)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (post is null)
        {
            return NotFound();
        }

        input.Id = post.Id;
        input.CoverUrl = post.Cover?.Url;
        var originalCategoryId = post.CategoryId;
        var version = new byte[8];

        if (!Convert.TryFromBase64String(input.Version ?? "", version, out var length)
            || length != version.Length
            || !post.Version.AsSpan().SequenceEqual(version))
        {
            ModelState.AddModelError(
                string.Empty,
                "Bài viết đã thay đổi hoặc phiên sửa đã cũ. "
                + "Anh hãy mở lại bài từ danh sách để đối chiếu.");
        }

        if (!ModelState.IsValid)
        {
            return await ShowFormAsync(
                nameof(Edit), input, cancellationToken, originalCategoryId);
        }

        var categoryId = input.CategoryId!.Value;

        if (categoryId != originalCategoryId
            && !await IsActiveCategoryAsync(categoryId, cancellationToken))
        {
            ModelState.AddModelError(
                nameof(input.CategoryId),
                "Danh mục này không còn hoạt động. Anh hãy chọn danh mục khác.");

            return await ShowFormAsync(
                nameof(Edit), input, cancellationToken, originalCategoryId);
        }

        var cleanHtml = ValidatePostContent(input.ContentHtml);

        if (!ModelState.IsValid)
        {
            return await ShowFormAsync(
                nameof(Edit), input, cancellationToken, originalCategoryId);
        }

        post.Slug = await SlugHelper.ResolveAsync(
            input.Slug,
            input.Title,
            _context.Set<Post>()
                .Where(other => other.Id != id)
                .Select(other => other.Slug),
            cancellationToken);

        post.Title = input.Title.Trim();
        post.CategoryId = categoryId;
        post.Summary = string.IsNullOrWhiteSpace(input.Summary)
            ? null
            : input.Summary.Trim();
        post.ContentHtml = cleanHtml;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Bài viết vừa được cập nhật hoặc xóa. "
                + "Anh hãy mở lại bài từ danh sách để kiểm tra.");

            return await ShowFormAsync(
                nameof(Edit), input, cancellationToken, originalCategoryId);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(
                nameof(input.Slug),
                "Slug này đã được dùng. Anh hãy nhập slug khác hoặc để trống để tự tạo.");

            return await ShowFormAsync(
                nameof(Edit), input, cancellationToken, originalCategoryId);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 547 })
        {
            ModelState.AddModelError(
                nameof(input.CategoryId),
                "Danh mục vừa thay đổi. Anh hãy kiểm tra và chọn lại.");

            return await ShowFormAsync(
                nameof(Edit), input, cancellationToken, originalCategoryId);
        }

        TempData["PostSuccess"] = "Đã cập nhật bài viết.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost("{id:long}/publication")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPublication(
    [FromRoute] long id,
    [FromForm] bool? publish,
    [FromForm] string? version,
    CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || publish is null)
        {
            return BadRequest("Thao tác xuất bản không hợp lệ.");
        }

        var post = await _context.Set<Post>()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (post is null)
        {
            return NotFound();
        }

        if (!TryReadPostVersion(version, out var originalVersion)
            || !post.Version.AsSpan().SequenceEqual(originalVersion))
        {
            TempData["PostError"] =
                "Bài viết vừa thay đổi. Anh hãy kiểm tra lại trạng thái rồi thử lại.";

            return RedirectToAction(nameof(Index));
        }

        if (publish.Value
            && !await IsActiveCategoryAsync(post.CategoryId, cancellationToken))
        {
            TempData["PostError"] =
                "Danh mục của bài đã bị ẩn. Anh hãy chọn danh mục đang hoạt động trước khi xuất bản.";

            return RedirectToAction(nameof(Index));
        }

        if (publish.Value && post.Status != "PUBLISHED")
        {
            post.Status = "PUBLISHED";
            post.PublishedAtUtc = DateTime.UtcNow;
        }
        else if (!publish.Value)
        {
            post.Status = "DRAFT";
            post.PublishedAtUtc = null;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);

            TempData["PostSuccess"] = publish.Value
                ? "Đã xuất bản bài viết."
                : "Đã chuyển bài viết về bản nháp.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["PostError"] =
                "Bài viết vừa được sửa hoặc xóa. Anh hãy kiểm tra danh sách rồi thử lại.";
        }

        return RedirectToAction(nameof(Index));
    }
    [HttpPost("{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
    [FromRoute] long id,
    [FromForm] string? version,
    CancellationToken cancellationToken)
    {
        if (!TryReadPostVersion(version, out var originalVersion))
        {
            return BadRequest("Phiên bản bài viết không hợp lệ.");
        }

        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
            SET XACT_ABORT ON;

            BEGIN TRY
                BEGIN TRANSACTION;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.Posts WITH (UPDLOCK, HOLDLOCK)
                    WHERE Id = {id} AND [Version] = {originalVersion}
                )
                BEGIN
                    THROW 51001, 'Post was changed or deleted.', 1;
                END;

                UPDATE dbo.PostComments
                SET ParentId = NULL
                WHERE PostId = {id} AND ParentId IS NOT NULL;

                DELETE FROM dbo.PostComments WHERE PostId = {id};
                DELETE FROM dbo.PostTags WHERE PostId = {id};
                DELETE FROM dbo.Posts WHERE Id = {id};

                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0
                    ROLLBACK TRANSACTION;

                THROW;
            END CATCH;
            """, cancellationToken);

            TempData["PostSuccess"] = "Đã xóa bài viết và các bình luận của bài.";
        }
        catch (SqlException exception) when (exception.Number == 51001)
        {
            TempData["PostError"] =
                "Bài viết đã thay đổi hoặc đã bị xóa. Anh hãy kiểm tra lại danh sách.";
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            TempData["PostError"] =
                "Bài viết đang được dữ liệu khác tham chiếu. Thao tác xóa đã được hủy.";
        }

        return RedirectToAction(nameof(Index));
    }
    [HttpPost("{id:long}/cover")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadCover(
    [FromRoute] long id,
    [FromForm] string? version,
    [FromForm] IFormFile? coverImage,
    [FromServices] IWebHostEnvironment environment,
    [FromServices] ILogger<PostsController> logger,
    CancellationToken cancellationToken)
    {
        const int maxFileSize = 5 * 1024 * 1024;

        var post = await _context.Set<Post>()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (post is null)
        {
            return NotFound();
        }

        if (!TryReadPostVersion(version, out var originalVersion)
            || !post.Version.AsSpan().SequenceEqual(originalVersion))
        {
            TempData["PostError"] =
                "Bài viết vừa thay đổi. Anh hãy chọn lại ảnh trên phiên bản mới nhất.";

            return RedirectToAction(nameof(Edit), new { id });
        }

        if (coverImage is null || coverImage.Length < 8 || coverImage.Length > maxFileSize)
        {
            TempData["PostError"] = "Anh hãy chọn một ảnh JPG hoặc PNG tối đa 5 MB.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var originalName = Path.GetFileName(coverImage.FileName);
        var extension = Path.GetExtension(originalName).ToLowerInvariant();

        await using var input = coverImage.OpenReadStream();

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
            TempData["PostError"] = "File tải lên phải đúng định dạng JPG hoặc PNG.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var fileName = $"{Guid.NewGuid():N}{(isPng ? ".png" : ".jpg")}";
        var folder = Path.Combine(environment.WebRootPath, "uploads", "posts");
        var filePath = Path.Combine(folder, fileName);
        var tempPath = filePath + ".tmp";

        try
        {
            Directory.CreateDirectory(folder);

            await using (var output = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await output.WriteAsync(header.AsMemory(), cancellationToken);
                await input.CopyToAsync(output, cancellationToken);
            }

            System.IO.File.Move(tempPath, filePath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not write the cover for post {PostId}.", id);

            TempData["PostError"] = "Không ghi được ảnh bìa. Anh hãy thử lại.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        finally
        {
            TryDeleteUpload(tempPath, logger);
        }

        var actorClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        post.Cover = new MediaFile
        {
            Url = $"~/uploads/posts/{fileName}",
            OriginalName = originalName.Length > 255 ? originalName[..255] : originalName,
            MimeType = isPng ? "image/png" : "image/jpeg",
            SizeBytes = coverImage.Length,
            AltText = post.Title,
            UploadedBy = long.TryParse(actorClaim, out var actorId) ? actorId : null,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            TempData["PostSuccess"] = "Đã cập nhật ảnh bìa.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TryDeleteUpload(filePath, logger);

            TempData["PostError"] =
                "Bài viết vừa thay đổi hoặc bị xóa trong lúc tải ảnh.";
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 547 })
        {
            TryDeleteUpload(filePath, logger);
            TempData["PostError"] = "Dữ liệu liên quan vừa thay đổi. Anh hãy thử lại.";
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("import-html")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(512 * 1024)]
    public IActionResult ImportHtml([FromForm] string? html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > 100_000)
        {
            return BadRequest(new
            {
                message = "Anh hãy nhập mã HTML, tối đa 100.000 ký tự."
            });
        }

        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.IntersectWith(new[]
        {
            "h1", "h2", "h3", "h4", "h5", "h6",
            "div", "p", "span", "br", "hr", "blockquote",
            "b", "strong", "i", "em", "u", "s", "sub", "sup",
            "ul", "ol", "li", "a", "img",
            "table", "caption", "thead", "tbody", "tfoot", "tr", "th", "td"
        });

        sanitizer.AllowedAttributes.IntersectWith(new[]
        {
            "href", "src", "alt", "title", "style",
            "width", "height", "colspan", "rowspan"
        });

        sanitizer.AllowedCssProperties.IntersectWith(new[]
        {
            "color", "background-color", "font-family", "font-size",
            "font-weight", "font-style", "text-align", "text-decoration",
            "line-height", "margin", "padding", "border", "border-collapse",
            "width", "max-width", "height"
        });

        var cleanHtml = sanitizer.Sanitize(html).Trim();

        if (string.IsNullOrWhiteSpace(cleanHtml))
        {
            return BadRequest(new
            {
                message = "Mã HTML không có nội dung được hỗ trợ."
            });
        }

        return Ok(new { html = cleanHtml });
    }

    private string ValidatePostContent(string contentHtml)
    {
        var cleanHtml = new HtmlSanitizer().Sanitize(contentHtml.Trim());
        var body = new HtmlParser().ParseDocument(cleanHtml).Body;

        if (string.IsNullOrWhiteSpace(body?.TextContent))
        {
            ModelState.AddModelError(
                nameof(PostFormInput.ContentHtml),
                "Bài viết cần có nội dung văn bản.");
        }

        return cleanHtml;
    }

    private Task<bool> IsActiveCategoryAsync(
        long categoryId,
        CancellationToken cancellationToken)
    {
        return _context.Set<PostCategory>().AnyAsync(
            category => category.Id == categoryId && category.IsActive,
            cancellationToken);
    }

    private async Task<IActionResult> ShowFormAsync(
        string viewName,
        PostFormInput input,
        CancellationToken cancellationToken,
        long? currentCategoryId = null)
    {
        var categories = await _context.Set<PostCategory>()
            .AsNoTracking()
            .Where(category => category.IsActive || category.Id == currentCategoryId)
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new
            {
                category.Id,
                Name = category.IsActive
                    ? category.Name
                    : category.Name + " (đã ẩn)"
            })
            .ToListAsync(cancellationToken);

        input.Categories = new SelectList(categories, "Id", "Name");

        return View(viewName, input);
    }
    private static bool TryReadPostVersion(string? value, out byte[] version)
    {
        version = new byte[8];

        return Convert.TryFromBase64String(value ?? "", version, out var length)
            && length == version.Length;
    }

    private static void TryDeleteUpload(
        string path,
        ILogger<PostsController> logger)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not clean up upload {UploadPath}.", path);
        }
    }
    public sealed record PostRow(
     long Id,
     string Title,
     string Slug,
     string CategoryName,
     string AuthorName,
     string? CoverUrl,
     string Status,
     DateTime? PublishedAtUtc,
     byte[] Version);

    public sealed record CategoryOption(
        long Id,
        string Name,
        bool IsActive);

    public sealed record IndexViewModel(
        IReadOnlyList<PostRow> Posts,
        IReadOnlyList<CategoryOption> Categories,
        string? Keyword,
        long? CategoryId,
        string? Status,
        int PageNumber,
        int TotalPages,
        int TotalPosts);
}
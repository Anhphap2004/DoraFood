using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using Microsoft.Data.SqlClient;

namespace DoraFood.Controllers;

[Authorize]
[Route("tai-khoan")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ProfileController : Controller
{
    private readonly DoraFoodContext _context;

    public ProfileController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet(Name = "DoraFood.MyProfile")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(userIdClaim, out var userId))
        {
            return Forbid();
        }

        var account = await (
            from user in _context.Set<User>().AsNoTracking()
            where user.Id == userId && user.IsActive
            join profile in _context.Set<UserProfile>().AsNoTracking()
                on user.Id equals profile.UserId into profiles
            from profile in profiles.DefaultIfEmpty()
            select new
            {
                user.UserName,
                user.Email,
                user.EmailConfirmed,
                user.CreatedAtUtc,
                FullName = profile == null ? null : profile.FullName,
                PhoneNumber = profile == null ? null : profile.PhoneNumber,
                DateOfBirth = profile == null ? null : profile.DateOfBirth,
                Gender = profile == null ? null : profile.Gender,
                AvatarUrl = profile == null ? null : profile.AvatarUrl
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return Forbid();
        }

        var model = new UserProfileViewModel(
            account.UserName,
            account.Email,
            account.EmailConfirmed,
            account.FullName,
            account.PhoneNumber,
            account.DateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            account.Gender,
            account.AvatarUrl,
            account.CreatedAtUtc);

        return View(model);
    }
    [HttpGet("chinh-sua", Name = "DoraFood.EditMyProfile")]
    public async Task<IActionResult> Edit(CancellationToken cancellationToken)
    {
        var userId = await GetActiveUserIdAsync(cancellationToken);

        if (userId is not long id)
        {
            return Forbid();
        }

        var profile = await _context.Set<UserProfile>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == id, cancellationToken);

        return View(new EditProfileInput
        {
            FullName = profile?.FullName ?? string.Empty,
            PhoneNumber = profile?.PhoneNumber,
            DateOfBirth = profile?.DateOfBirth,
            Gender = profile?.Gender,
            CurrentGender = profile?.Gender
        });
    }

    [HttpPost("chinh-sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        EditProfileInput input,
        CancellationToken cancellationToken)
    {
        var userId = await GetActiveUserIdAsync(cancellationToken);

        if (userId is not long id)
        {
            return Forbid();
        }

        var profile = await _context.Set<UserProfile>()
            .AsTracking()
            .SingleOrDefaultAsync(item => item.UserId == id, cancellationToken);

        input.CurrentGender = profile?.Gender;

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

        if (input.DateOfBirth is { } birthDate && birthDate > today)
        {
            ModelState.AddModelError(
                nameof(input.DateOfBirth),
                "Ngày sinh không được nằm trong tương lai.");
        }

        if (input.Gender != input.CurrentGender
            && input.Gender is not (null or "MALE" or "FEMALE" or "OTHER"))
        {
            ModelState.AddModelError(
                nameof(input.Gender),
                "Anh hãy chọn giới tính trong danh sách.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var isNewProfile = profile is null;

        if (profile is null)
        {
            profile = new UserProfile { UserId = id };
            _context.Set<UserProfile>().Add(profile);
        }

        profile.FullName = input.FullName.Trim();
        profile.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber)
            ? null
            : input.PhoneNumber.Trim();
        profile.DateOfBirth = input.DateOfBirth;
        profile.Gender = input.Gender;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (isNewProfile
                  && exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(
                string.Empty,
                "Hồ sơ vừa được tạo ở tab khác. Anh hãy mở lại trang chỉnh sửa trước khi lưu.");

            return View(input);
        }

        TempData["ProfileSuccess"] = "Đã cập nhật hồ sơ cá nhân.";
        return RedirectToRoute("DoraFood.MyProfile");
    }

    private async Task<long?> GetActiveUserIdAsync(
        CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(claim, out var userId))
        {
            return null;
        }

        var isActive = await _context.Set<User>()
            .AnyAsync(
                item => item.Id == userId && item.IsActive,
                cancellationToken);

        return isActive ? userId : null;
    }
    private const int MaxAvatarBytes = 2 * 1024 * 1024;
    private const string AvatarUrlPrefix = "~/tai-khoan/anh-dai-dien/";

    [HttpGet("anh-dai-dien", Name = "DoraFood.ChangeMyAvatar")]
    public async Task<IActionResult> Avatar(CancellationToken cancellationToken)
    {
        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        var profile = await _context.Set<UserProfile>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (profile is null)
        {
            TempData["ProfileError"] =
                "Anh hãy lưu họ tên trong mục Chỉnh sửa hồ sơ trước khi đổi ảnh.";

            return RedirectToRoute("DoraFood.MyProfile");
        }

        return View("Avatar", profile.AvatarUrl);
    }

    [HttpPost("anh-dai-dien")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> Avatar(
        IFormFile? avatar,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] ILogger<ProfileController> logger,
        CancellationToken cancellationToken)
    {
        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        var profile = await _context.Set<UserProfile>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (profile is null)
        {
            TempData["ProfileError"] =
                "Anh hãy lưu họ tên trong mục Chỉnh sửa hồ sơ trước khi đổi ảnh.";

            return RedirectToRoute("DoraFood.MyProfile");
        }

        if (avatar is null || avatar.Length < 8 || avatar.Length > MaxAvatarBytes)
        {
            ModelState.AddModelError(
                string.Empty, "Anh hãy chọn ảnh JPG hoặc PNG tối đa 2 MB.");

            return View("Avatar", profile.AvatarUrl);
        }

        using var buffer = new MemoryStream((int)avatar.Length);
        await avatar.CopyToAsync(buffer, cancellationToken);

        var bytes = buffer.ToArray();
        var extension = Path.GetExtension(avatar.FileName).ToLowerInvariant();

        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] jpegSignature = [0xFF, 0xD8, 0xFF];

        var isPng = extension == ".png"
            && bytes.AsSpan().StartsWith(pngSignature);

        var isJpeg = (extension is ".jpg" or ".jpeg")
            && bytes.AsSpan().StartsWith(jpegSignature);

        if (!isPng && !isJpeg)
        {
            ModelState.AddModelError(
                string.Empty, "Tệp đã chọn không đúng định dạng JPG hoặc PNG.");

            return View("Avatar", profile.AvatarUrl);
        }

        var fileName = $"{Guid.NewGuid():N}{(isPng ? ".png" : ".jpg")}";
        var folder = GetAvatarFolder(environment, userId);
        var filePath = Path.Combine(folder, fileName);
        var newUrl = AvatarUrlPrefix + fileName;
        var previousUrl = profile.AvatarUrl;

        try
        {
            Directory.CreateDirectory(folder);
            await System.IO.File.WriteAllBytesAsync(
                filePath, bytes, cancellationToken);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Không ghi được ảnh đại diện của tài khoản {UserId}.", userId);
            DeleteAvatarFile(filePath, logger);

            ModelState.AddModelError(
                string.Empty, "Không lưu được ảnh. Anh hãy thử lại.");

            return View("Avatar", previousUrl);
        }

        var updatedAt = DateTime.UtcNow;

        var updatedRows = await _context.Set<UserProfile>()
            .Where(item => item.UserId == userId && item.AvatarUrl == previousUrl)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.AvatarUrl, newUrl)
                .SetProperty(item => item.UpdatedAtUtc, updatedAt),
                cancellationToken);

        if (updatedRows == 0)
        {
            DeleteAvatarFile(filePath, logger);

            TempData["ProfileError"] =
                "Ảnh đại diện vừa thay đổi ở tab khác. Anh hãy chọn lại ảnh.";

            return RedirectToRoute("DoraFood.MyProfile");
        }

        if (previousUrl is not null
            && previousUrl.StartsWith(AvatarUrlPrefix, StringComparison.Ordinal))
        {
            var previousFileName = previousUrl[AvatarUrlPrefix.Length..];

            if (IsAvatarFileName(previousFileName))
            {
                DeleteAvatarFile(Path.Combine(folder, previousFileName), logger);
            }
        }

        TempData["ProfileSuccess"] = "Đã cập nhật ảnh đại diện.";
        return RedirectToRoute("DoraFood.MyProfile");
    }

    [HttpGet("anh-dai-dien/{fileName}")]
    public async Task<IActionResult> AvatarFile(
        [FromRoute] string fileName,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!IsAvatarFileName(fileName))
        {
            return NotFound();
        }

        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        var expectedUrl = AvatarUrlPrefix + fileName;

        var isCurrentAvatar = await _context.Set<UserProfile>()
            .AnyAsync(
                item => item.UserId == userId && item.AvatarUrl == expectedUrl,
                cancellationToken);

        if (!isCurrentAvatar)
        {
            return NotFound();
        }

        var path = Path.Combine(GetAvatarFolder(environment, userId), fileName);

        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var contentType = fileName.EndsWith(".png", StringComparison.Ordinal)
            ? "image/png"
            : "image/jpeg";

        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return PhysicalFile(path, contentType);
    }

    private static string GetAvatarFolder(
        IWebHostEnvironment environment,
        long userId)
    {
        return Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "profile-avatars",
            userId.ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsAvatarFileName(string fileName)
    {
        return (Path.GetExtension(fileName) is ".jpg" or ".png")
            && Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _);
    }

    private static void DeleteAvatarFile(
        string path,
        ILogger<ProfileController> logger)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Không dọn được tệp ảnh đại diện {Path}.", path);
        }
    }
}
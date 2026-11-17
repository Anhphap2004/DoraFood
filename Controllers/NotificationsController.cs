using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController : Controller
{
    private readonly DoraFoodContext _context;

    private long _userId;

    public NotificationsController(DoraFoodContext context)
    {
        _context = context;
    }

    // Kiểm tra tài khoản và phiên đăng nhập trước mỗi action.
    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var idClaim = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        var stampClaim = User.FindFirstValue(
            "SecurityStamp");

        if (!long.TryParse(idClaim, out var userId)
            || userId <= 0
            || !Guid.TryParse(stampClaim, out var securityStamp))
        {
            context.Result = Unauthorized();
            return;
        }

        var validAccount = await _context.Set<User>()
            .AsNoTracking()
            .AnyAsync(
                user =>
                    user.Id == userId
                    && user.IsActive
                    && user.SecurityStamp == securityStamp,
                HttpContext.RequestAborted);

        if (!validAccount)
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            context.Result = Unauthorized();
            return;
        }

        _userId = userId;

        await next();
    }

    // GET: lấy dữ liệu cho chuông thông báo.
    [HttpGet(
        "/thong-bao/moi-nhat",
        Name = "DoraFoodNotificationsLatest")]
    public async Task<IActionResult> Latest()
    {
        var cancellationToken = HttpContext.RequestAborted;

        var query = _context.Set<Notification>()
            .AsNoTracking()
            .Where(notification =>
                notification.UserId == _userId);

        var unreadCount = await query.CountAsync(
            notification => notification.ReadAtUtc == null,
            cancellationToken);

        var notifications = await query
            .OrderByDescending(notification =>
                notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id)
            .Take(10)
            .Select(notification => new
            {
                notification.Id,
                notification.Title,
                notification.Body,
                notification.CreatedAtUtc,
                notification.ReadAtUtc
            })
            .ToListAsync(cancellationToken);

        var items = notifications.Select(notification => new
        {
            // Trả ID dạng chuỗi để JavaScript không làm tròn
            // khi ID bigint tăng đến giá trị lớn.
            id = notification.Id.ToString(),

            title = notification.Title,
            body = notification.Body,

            isRead = notification.ReadAtUtc.HasValue,

            // SQL datetime2 không lưu múi giờ.
            // Gắn UTC rõ ràng trước khi gửi cho trình duyệt.
            createdAtUtc = DateTime.SpecifyKind(
                    notification.CreatedAtUtc,
                    DateTimeKind.Utc)
                .ToString("O")
        });

        return Json(new
        {
            unreadCount,
            items
        });
    }

    // POST: đánh dấu một thông báo đã đọc.
    [HttpPost(
        "/thong-bao/{id:long}/da-doc",
        Name = "DoraFoodReadNotification")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(
        [FromRoute] long id)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return BadRequest(new
            {
                message = "Thông báo không hợp lệ."
            });
        }

        var cancellationToken = HttpContext.RequestAborted;

        // Luôn lọc cả ID thông báo và người nhận.
        var notification = await _context.Set<Notification>()
            .AsNoTracking()
            .Where(item =>
                item.Id == id
                && item.UserId == _userId)
            .Select(item => new
            {
                item.Id,
                item.LinkUrl
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (notification == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy thông báo."
            });
        }

        // Không ghi đè thời điểm đọc đầu tiên.
        await _context.Set<Notification>()
            .Where(item =>
                item.Id == id
                && item.UserId == _userId
                && item.ReadAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    item => item.ReadAtUtc,
                    (DateTime?)DateTime.UtcNow),
                cancellationToken);

        var unreadCount = await CountUnreadAsync();

        // Chỉ cho phép mở đường dẫn nội bộ.
        var linkUrl = Url.IsLocalUrl(notification.LinkUrl)
            ? notification.LinkUrl
            : null;

        return Json(new
        {
            id = notification.Id.ToString(),
            unreadCount,
            linkUrl
        });
    }

    // POST: đánh dấu tất cả thông báo hiện có đã đọc.
    [HttpPost(
        "/thong-bao/doc-tat-ca",
        Name = "DoraFoodReadAllNotifications")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAll()
    {
        var cancellationToken = HttpContext.RequestAborted;

        // Chốt mốc ID trước khi cập nhật.
        // Thông báo mới phát sinh sau mốc này vẫn chưa đọc.
        var lastId = await _context.Set<Notification>()
            .Where(item => item.UserId == _userId)
            .MaxAsync(
                item => (long?)item.Id,
                cancellationToken);

        if (lastId.HasValue)
        {
            var now = DateTime.UtcNow;

            await _context.Set<Notification>()
                .Where(item =>
                    item.UserId == _userId
                    && item.Id <= lastId.Value
                    && item.ReadAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.ReadAtUtc,
                        (DateTime?)now),
                    cancellationToken);
        }

        return Json(new
        {
            unreadCount = await CountUnreadAsync()
        });
    }

    private Task<int> CountUnreadAsync()
    {
        return _context.Set<Notification>()
            .CountAsync(
                notification =>
                    notification.UserId == _userId
                    && notification.ReadAtUtc == null,
                HttpContext.RequestAborted);
    }
}
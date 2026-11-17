using System;
using DoraFood.Data;
using DoraFood.Models;

namespace DoraFood.Services;

public sealed class NotificationService
{
    private readonly DoraFoodContext _context;

    public NotificationService(DoraFoodContext context)
    {
        _context = context;
    }

    public Notification? Add(
        long? userId,
        string title,
        string body,
        string? linkUrl = null)
    {
        // Lượt đặt bàn không liên kết tài khoản
        // thì chưa thể gửi thông báo trong tài khoản.
        if (!userId.HasValue || userId.Value <= 0)
        {
            return null;
        }

        title = title.Trim();
        body = body.Trim();

        linkUrl = string.IsNullOrWhiteSpace(linkUrl)
            ? null
            : linkUrl.Trim();

        if (title.Length == 0 || title.Length > 200)
        {
            throw new ArgumentException(
                "Tiêu đề thông báo phải có từ 1 đến 200 ký tự.",
                nameof(title));
        }

        if (body.Length == 0 || body.Length > 2000)
        {
            throw new ArgumentException(
                "Nội dung thông báo phải có từ 1 đến 2000 ký tự.",
                nameof(body));
        }

        // Chỉ nhận đường dẫn nội bộ của website.
        if (linkUrl != null
            && (
                linkUrl.Length > 1000
                || !linkUrl.StartsWith("/")
                || linkUrl.StartsWith("//")
                || linkUrl.Contains('\\')
                || linkUrl.Contains('\r')
                || linkUrl.Contains('\n')))
        {
            throw new ArgumentException(
                "Đường dẫn thông báo phải là đường dẫn nội bộ hợp lệ.",
                nameof(linkUrl));
        }

        var notification = new Notification
        {
            UserId = userId.Value,
            Title = title,
            Body = body,
            LinkUrl = linkUrl,
            ReadAtUtc = null,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Set<Notification>().Add(notification);

        return notification;
    }
}
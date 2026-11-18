using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

[Authorize]
[ResponseCache(
    Duration = 0,
    Location = ResponseCacheLocation.None,
    NoStore = true)]
public sealed class ChatImagesController : Controller
{
    private readonly DoraFoodContext _context;
    private readonly ChatService _chat;
    private readonly string _imageDirectory;

    public ChatImagesController(
        DoraFoodContext context,
        ChatService chat,
        IWebHostEnvironment environment)
    {
        _context = context;
        _chat = chat;

        _imageDirectory = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "ChatImages");
    }

    // id là ID tin nhắn, không phải ID ảnh.
    // Ví dụ: /tro-chuyen/anh/25
    [HttpGet(
        "/tro-chuyen/anh/{id:long}",
        Name = "DoraFoodChatImage")]
    public async Task<IActionResult> Show([FromRoute] long id)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return NotFound();
        }

        var cancellationToken = HttpContext.RequestAborted;

        try
        {
            // Kiểm tra tài khoản còn hoạt động,
            // phiên đăng nhập và quyền thực tế trong SQL.
            var actor = await _chat.GetActorAsync(
                User,
                cancellationToken);

            // Chỉ lấy ảnh nếu có quyền xem hội thoại.
            var storageKey = await _context.Set<ChatMessage>()
                .AsNoTracking()
                .Where(message =>
                    message.Id == id
                    && message.MediaFileId != null
                    && message.MediaFile != null
                    && message.MediaFile.MimeType == "image/png"
                    && (
                        actor.IsStaff
                        || message.Conversation.CustomerId == actor.Id))
                .Select(message => message.MediaFile!.Url)
                .SingleOrDefaultAsync(cancellationToken);

            const string prefix = "chat-private/";

            if (string.IsNullOrWhiteSpace(storageKey)
                || !storageKey.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                return NotFound();
            }

            var fileName = storageKey[prefix.Length..];

            // Chỉ chấp nhận tên ảnh theo định dạng
            // do ChatService tạo ra ở bước trước.
            var validFileName = Regex.IsMatch(
                fileName,
                @"\A[0-9]+-[0-9]+-[0-9a-f]{32}-[0-9A-F]{64}\.png\z",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

            if (!validFileName)
            {
                return NotFound();
            }

            var filePath = Path.Combine(
                _imageDirectory,
                fileName);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            // Yêu cầu trình duyệt dùng đúng loại nội dung.
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            // Hiển thị ảnh trực tiếp trong khung chat.
            return PhysicalFile(
                filePath,
                "image/png");
        }
        catch (ChatException exception)
        {
            return StatusCode(
                exception.StatusCode,
                new
                {
                    message = exception.Message
                });
        }
    }
}
using System;
using System.Linq;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.Services;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Controllers;

[Authorize]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class ChatController : Controller
{
    private readonly DoraFoodContext _context;
    private readonly ChatService _chat;
    private readonly ILogger<ChatController> _logger;

    private long _userId;

    public ChatController(
        DoraFoodContext context,
        ChatService chat,
        ILogger<ChatController> logger)
    {
        _context = context;
        _chat = chat;
        _logger = logger;
    }

    // Kiểm tra tài khoản và phiên đăng nhập trước mỗi action.
    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        try
        {
            var actor = await _chat.GetActorAsync(
                User,
                HttpContext.RequestAborted);

            _userId = actor.Id;
        }
        catch (ChatException ex)
        {
            context.Result = StatusCode(
                ex.StatusCode,
                new { message = ex.Message });

            return;
        }

        await next();
    }

    // POST: mở hội thoại chung hoặc theo lượt đặt bàn.
    [HttpPost(
        "/tro-chuyen/mo",
        Name = "DoraFoodOpenChat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(
        [FromForm] long? reservationId)
    {
        if (!ModelState.IsValid
            || (reservationId.HasValue && reservationId.Value <= 0))
        {
            return BadRequest(new
            {
                message = "Thông tin đặt bàn không hợp lệ."
            });
        }

        try
        {
            var conversation = await _chat.OpenConversationAsync(
                User,
                reservationId,
                HttpContext.RequestAborted);

            return Json(new
            {
                conversationId = conversation.Id.ToString(),

                url = Url.RouteUrl(
                    "DoraFoodChatDetails",
                    new { id = conversation.Id })
            });
        }
        catch (ChatException ex)
        {
            return StatusCode(
                ex.StatusCode,
                new { message = ex.Message });
        }
        catch (DbUpdateException ex)
        {
            return ChatStorageError(ex);
        }
        catch (SqlException ex)
        {
            return ChatStorageError(ex);
        }
    }

    // GET: trang trò chuyện của khách.
    [HttpGet(
        "/tro-chuyen/{id:long}",
        Name = "DoraFoodChatDetails")]
    public async Task<IActionResult> Details(
        [FromRoute] long id)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return BadRequest("Hội thoại không hợp lệ.");
        }

        var model = await MyConversations()
            .Where(conversation => conversation.Id == id)
            .Select(conversation => new CustomerChatViewModel
            {
                ConversationId = conversation.Id,
                Title = conversation.Title,
                Status = conversation.Status,
                ReservationId = conversation.ReservationId,

                ReservationCode = conversation.Reservation == null
                    ? null
                    : conversation.Reservation.Code
            })
            .SingleOrDefaultAsync(
                HttpContext.RequestAborted);

        if (model == null)
        {
            return NotFound("Không tìm thấy hội thoại.");
        }

        ViewData["Title"] = "Trò chuyện cùng DoraFood";

        return View(model);
    }

    // GET: lấy 50 tin gần nhất hoặc tải các tin cũ hơn.
    [HttpGet(
        "/tro-chuyen/{id:long}/tin-nhan",
        Name = "DoraFoodChatMessages")]
    public async Task<IActionResult> Messages(
        [FromRoute] long id,
        [FromQuery] long? beforeId)
    {
        if (!ModelState.IsValid
            || id <= 0
            || (beforeId.HasValue && beforeId.Value <= 0))
        {
            return BadRequest(new
            {
                message = "Thông tin tải tin nhắn không hợp lệ."
            });
        }

        var cancellationToken = HttpContext.RequestAborted;

        var ownsConversation = await MyConversations()
            .AnyAsync(
                conversation => conversation.Id == id,
                cancellationToken);

        if (!ownsConversation)
        {
            return NotFound(new
            {
                message = "Không tìm thấy hội thoại."
            });
        }

        var query = _context.Set<ChatMessage>()
            .AsNoTracking()
            .Where(message => message.ConversationId == id);

        if (beforeId.HasValue)
        {
            query = query.Where(
                message => message.Id < beforeId.Value);
        }

        // Đọc dư một tin để biết còn lịch sử phía trước không.
        var rows = await query
            .OrderByDescending(message => message.Id)
            .Take(51)
            .ToListAsync(cancellationToken);

        var hasOlder = rows.Count > 50;

        var messages = rows
            .Take(50)
            .OrderBy(message => message.Id)
            .ToList();

        return Json(new
        {
            hasOlder,

            oldestId = messages.Count > 0
                ? messages[0].Id.ToString()
                : null,

            items = messages.Select(ToMessageResponse)
        });
    }

    // POST: khách gửi tin.
    [HttpPost(
        "/tro-chuyen/{id:long}/gui",
        Name = "DoraFoodSendChatMessage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        [FromRoute] long id,
        [FromForm] SendChatMessageInput input)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            var message = ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault(text =>
                    !string.IsNullOrWhiteSpace(text));

            return BadRequest(new
            {
                message = message
                    ?? "Thông tin gửi tin nhắn không hợp lệ."
            });
        }

        try
        {
            var message = await _chat.SendAsync(
                User,
                id,
                input.Body,
                input.ClientMessageId,

                // Cố định phía server.
                // Khách không thể tự gửi với vai trò nhân viên.
                fromStaff: false,

                cancellationToken: HttpContext.RequestAborted);

            return Json(new
            {
                item = ToMessageResponse(message)
            });
        }
        catch (ChatException ex)
        {
            return StatusCode(
                ex.StatusCode,
                new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Hội thoại {ConversationId} đã thay đổi.",
                id);

            return Conflict(new
            {
                message =
                    "Hội thoại vừa thay đổi. " +
                    "Anh hãy tải lại thông tin rồi thử lại."
            });
        }
        catch (DbUpdateException ex)
        {
            return ChatStorageError(ex);
        }
        catch (SqlException ex)
        {
            return ChatStorageError(ex);
        }
    }

    // POST: khách xác nhận đã đọc các tin nhà hàng gửi.
    [HttpPost(
        "/tro-chuyen/{id:long}/da-doc",
        Name = "DoraFoodReadChatMessages")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(
        [FromRoute] long id,
        [FromForm] long upToId)
    {
        if (!ModelState.IsValid || id <= 0 || upToId <= 0)
        {
            return BadRequest(new
            {
                message = "Thông tin đánh dấu đã đọc không hợp lệ."
            });
        }

        var cancellationToken = HttpContext.RequestAborted;

        var ownsConversation = await MyConversations()
            .AnyAsync(
                conversation => conversation.Id == id,
                cancellationToken);

        if (!ownsConversation)
        {
            return NotFound(new
            {
                message = "Không tìm thấy hội thoại."
            });
        }

        // Chỉ đánh dấu các tin của nhà hàng đến mốc đã xem.
        // Tin mới hơn vẫn giữ trạng thái chưa đọc.
        var affected = await _context.Set<ChatMessage>()
            .Where(message =>
                message.ConversationId == id
                && message.SenderType == "STAFF"
                && message.Id <= upToId
                && message.ReadAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ReadAtUtc,
                    (DateTime?)DateTime.UtcNow),
                cancellationToken);

        return Json(new
        {
            success = true,
            updatedCount = affected
        });
    }

    private IQueryable<ChatConversation> MyConversations()
    {
        return _context.Set<ChatConversation>()
            .AsNoTracking()
            .Where(conversation =>
                conversation.CustomerId == _userId);
    }

    // Chỉ trả dữ liệu cần hiển thị,
    // không trả toàn bộ entity và các quan hệ.
    private object ToMessageResponse(ChatMessage message)
    {
        return new
        {
            id = message.Id.ToString(),
            senderType = message.SenderType,
            body = message.Body,
            isMine = message.SenderId == _userId,
            isRead = message.ReadAtUtc.HasValue,

            createdAtUtc = DateTime.SpecifyKind(
                    message.CreatedAtUtc,
                    DateTimeKind.Utc)
                .ToString("O")
        };
    }

    private IActionResult ChatStorageError(Exception exception)
    {
        _logger.LogError(
            exception,
            "Không xử lý được dữ liệu trò chuyện tại {Path}.",
            Request.Path);

        return StatusCode(503, new
        {
            message =
                "Chưa xử lý được tin nhắn lúc này. " +
                "Anh hãy thử lại."
        });
    }
}
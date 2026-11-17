using System;
using System.Linq;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
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

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class ChatController : Controller
{
    private readonly DoraFoodContext _context;
    private readonly ChatService _chat;
    private readonly ILogger<ChatController> _logger;

    private long _staffId;

    public ChatController(
        DoraFoodContext context,
        ChatService chat,
        ILogger<ChatController> logger)
    {
        _context = context;
        _chat = chat;
        _logger = logger;
    }

    // Kiểm tra lại tài khoản và quyền thực tế trong database.
    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        try
        {
            var actor = await _chat.GetActorAsync(
                User,
                HttpContext.RequestAborted);

            if (!actor.IsStaff)
            {
                context.Result = Forbid();
                return;
            }

            _staffId = actor.Id;
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

    // GET: danh sách hội thoại.
    [HttpGet(
        "/Admin/Chat",
        Name = "DoraFoodAdminChat")]
    public async Task<IActionResult> Index(
        string? keyword,
        string status = "OPEN",
        bool unreadOnly = false,
        int page = 1)
    {
        keyword = string.IsNullOrWhiteSpace(keyword)
            ? null
            : keyword.Trim();

        status = string.IsNullOrWhiteSpace(status)
            ? "OPEN"
            : status.Trim().ToUpperInvariant();

        if (!ModelState.IsValid
            || keyword?.Length > 100
            || (
                status != "OPEN"
                && status != "CLOSED"
                && status != "ALL"))
        {
            return BadRequest("Điều kiện tìm kiếm không hợp lệ.");
        }

        var cancellationToken = HttpContext.RequestAborted;
        const int pageSize = 20;

        var query = _context.Set<ChatConversation>()
            .AsNoTracking();

        if (status != "ALL")
        {
            query = query.Where(
                conversation => conversation.Status == status);
        }

        if (keyword != null)
        {
            query = query.Where(conversation =>
                conversation.Customer.UserName.Contains(keyword)
                || conversation.Title.Contains(keyword)
                || (
                    conversation.Reservation != null
                    && (
                        conversation.Reservation.Code.Contains(keyword)
                        || conversation.Reservation.GuestName.Contains(keyword)
                        || conversation.Reservation.GuestPhone.Contains(keyword))));
        }

        if (unreadOnly)
        {
            query = query.Where(conversation =>
                conversation.Messages.Any(message =>
                    message.SenderType == "CUSTOMER"
                    && message.ReadAtUtc == null));
        }

        var totalItems = await query.CountAsync(
            cancellationToken);

        var totalPages = (int)Math.Ceiling(
            totalItems / (double)pageSize);

        page = Math.Clamp(
            page,
            1,
            Math.Max(1, totalPages));

        var conversations = await query
            .OrderByDescending(conversation =>
                conversation.UpdatedAtUtc)
            .ThenByDescending(conversation => conversation.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(conversation => new AdminChatRow
            {
                Id = conversation.Id,

                CustomerName = conversation.Reservation == null
                    ? conversation.Customer.UserName
                    : conversation.Reservation.GuestName,

                ReservationCode = conversation.Reservation == null
                    ? null
                    : conversation.Reservation.Code,

                Status = conversation.Status,

                UpdatedAtUtc = conversation.UpdatedAtUtc,

                LastMessage = conversation.Messages
                    .OrderByDescending(message => message.Id)
                    .Select(message => message.Body)
                    .FirstOrDefault(),

                UnreadCount = conversation.Messages.Count(
                    message =>
                        message.SenderType == "CUSTOMER"
                        && message.ReadAtUtc == null)
            })
            .ToListAsync(cancellationToken);

        var model = new AdminChatIndexViewModel
        {
            Conversations = conversations,
            Keyword = keyword,
            Status = status,
            UnreadOnly = unreadOnly,
            CurrentPage = page,
            TotalPages = totalPages,
            TotalItems = totalItems
        };

        ViewData["Title"] = "Tin nhắn khách hàng";

        // JavaScript gọi để cập nhật danh sách mà không tải lại cả trang.
        if (Request.Headers["X-Requested-With"].ToString()
            == "XMLHttpRequest")
        {
            return Json(new
            {
                totalItems = model.TotalItems,
                totalPages = model.TotalPages,
                currentPage = model.CurrentPage,

                items = model.Conversations.Select(row => new
                {
                    id = row.Id.ToString(),
                    customerName = row.CustomerName,
                    reservationCode = row.ReservationCode,
                    status = row.Status,
                    preview = row.Preview,
                    unreadCount = row.UnreadCount,

                    updatedAtUtc = DateTime.SpecifyKind(
                        row.UpdatedAtUtc,
                        DateTimeKind.Utc).ToString("O")
                })
            });
        }

        // Khi mở trang bình thường, trả về giao diện.
        return View(model);
    }

    // GET: /Admin/Chat/15/thong-tin
    [HttpGet(
        "/Admin/Chat/{id:long}/thong-tin",
        Name = "DoraFoodAdminChatInfo")]
    public async Task<IActionResult> Info([FromRoute] long id)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return BadRequest(new
            {
                message = "Mã hội thoại không hợp lệ."
            });
        }

        var model = await _context.Set<ChatConversation>()
            .AsNoTracking()
            .Where(conversation => conversation.Id == id)
            .Select(conversation => new AdminChatDetailsViewModel
            {
                ConversationId = conversation.Id,
                Title = conversation.Title,
                Status = conversation.Status,

                CustomerName = conversation.Reservation == null
                    ? conversation.Customer.UserName
                    : conversation.Reservation.GuestName,

                CustomerUserName = conversation.Customer.UserName,

                GuestPhone = conversation.Reservation == null
                    ? null
                    : conversation.Reservation.GuestPhone,

                ReservationId = conversation.ReservationId,

                ReservationCode = conversation.Reservation == null
                    ? null
                    : conversation.Reservation.Code
            })
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        if (model == null)
        {
            return NotFound(new
            {
                message = "Hội thoại không tồn tại hoặc đã bị xóa."
            });
        }

        return Json(new
        {
            customerName = model.CustomerName,
            customerUserName = model.CustomerUserName,
            guestPhone = model.GuestPhone,
            reservationCode = model.ReservationCode,

            // Hội thoại đang mở mới được gửi tin nhắn.
            canSend = model.CanSend,

            bookingUrl = model.ReservationId.HasValue
                ? Url.RouteUrl(
                    "DoraFoodManageBookingDetails",
                    new
                    {
                        id = model.ReservationId.Value
                    })
                : null
        });
    }

    // GET: thông tin hội thoại để nhân viên trả lời.
    [HttpGet(
        "/Admin/Chat/{id:long}",
        Name = "DoraFoodAdminChatDetails")]
    public async Task<IActionResult> Details(
        [FromRoute] long id)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return BadRequest("Hội thoại không hợp lệ.");
        }

        var model = await _context.Set<ChatConversation>()
            .AsNoTracking()
            .Where(conversation => conversation.Id == id)
            .Select(conversation => new AdminChatDetailsViewModel
            {
                ConversationId = conversation.Id,
                Title = conversation.Title,
                Status = conversation.Status,

                CustomerName = conversation.Reservation == null
                    ? conversation.Customer.UserName
                    : conversation.Reservation.GuestName,

                CustomerUserName = conversation.Customer.UserName,

                GuestPhone = conversation.Reservation == null
                    ? null
                    : conversation.Reservation.GuestPhone,

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

        return RedirectToRoute(
     "DoraFoodAdminChat",
     new
     {
         conversationId = model.ConversationId
     });
    }

    // GET: lấy tin nhắn, hỗ trợ tải lịch sử cũ.
    [HttpGet(
        "/Admin/Chat/{id:long}/tin-nhan",
        Name = "DoraFoodAdminChatMessages")]
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

        var exists = await ConversationExistsAsync(id);

        if (!exists)
        {
            return NotFound(new
            {
                message = "Không tìm thấy hội thoại."
            });
        }

        var query = _context.Set<ChatMessage>()
            .AsNoTracking()
            .Include(message => message.Sender)
            .Where(message => message.ConversationId == id);

        if (beforeId.HasValue)
        {
            query = query.Where(
                message => message.Id < beforeId.Value);
        }

        var rows = await query
            .OrderByDescending(message => message.Id)
            .Take(51)
            .ToListAsync(cancellationToken);

        var messages = rows
            .Take(50)
            .OrderBy(message => message.Id)
            .ToList();

        return Json(new
        {
            hasOlder = rows.Count > 50,

            oldestId = messages.Count > 0
                ? messages[0].Id.ToString()
                : null,

            items = messages.Select(ToMessageResponse)
        });
    }

    // POST: nhân viên trả lời khách.
    [HttpPost(
        "/Admin/Chat/{id:long}/gui",
        Name = "DoraFoodAdminSendChatMessage")]
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
                fromStaff: true,
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
                "Hội thoại {ConversationId} vừa thay đổi.",
                id);

            return Conflict(new
            {
                message =
                    "Hội thoại vừa thay đổi. " +
                    "Anh tải lại thông tin rồi thử lại."
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

    // POST: nhà hàng đã đọc các tin khách gửi.
    [HttpPost(
        "/Admin/Chat/{id:long}/da-doc",
        Name = "DoraFoodAdminReadChatMessages")]
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

        if (!await ConversationExistsAsync(id))
        {
            return NotFound(new
            {
                message = "Không tìm thấy hội thoại."
            });
        }

        var affected = await _context.Set<ChatMessage>()
            .Where(message =>
                message.ConversationId == id
                && message.SenderType == "CUSTOMER"
                && message.Id <= upToId
                && message.ReadAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ReadAtUtc,
                    (DateTime?)DateTime.UtcNow),
                HttpContext.RequestAborted);

        return Json(new
        {
            success = true,
            updatedCount = affected
        });
    }

    private Task<bool> ConversationExistsAsync(long id)
    {
        return _context.Set<ChatConversation>()
            .AnyAsync(
                conversation => conversation.Id == id,
                HttpContext.RequestAborted);
    }

    private object ToMessageResponse(ChatMessage message)
    {
        var fromStaff = message.SenderType == "STAFF";

        return new
        {
            id = message.Id.ToString(),
            senderType = message.SenderType,

            senderName = fromStaff
                ? message.Sender?.UserName
                    ?? (message.SenderId == _staffId
                        ? User.Identity?.Name
                        : null)
                    ?? "Nhân viên nhà hàng"
                : "Khách hàng",

            body = message.Body,

            // Tin của các nhân viên cùng hiển thị
            // phía nhà hàng, bên phải khung chat.
            isMine = fromStaff,

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
            "Không xử lý được tin nhắn tại {Path}.",
            Request.Path);

        return StatusCode(503, new
        {
            message =
                "Chưa xử lý được tin nhắn lúc này. " +
                "Anh hãy thử lại."
        });
    }
}
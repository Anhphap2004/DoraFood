using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Services;

public sealed class ChatService
{
    private readonly DoraFoodContext _context;
    private readonly NotificationService _notifications;

    public ChatService(
        DoraFoodContext context,
        NotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    // Mở hội thoại của khách.
    // reservationId = null: tư vấn chung.
    // Có reservationId: tư vấn về lượt đặt bàn đó.
    public async Task<ChatConversation> OpenConversationAsync(
        ClaimsPrincipal user,
        long? reservationId,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(
            user,
            cancellationToken);

        if (reservationId.HasValue && reservationId.Value <= 0)
        {
            throw new ChatException(
                400,
                "Thông tin đặt bàn không hợp lệ.");
        }

        var resource =
            $"DoraFood.Chat.Open:{actor.Id}:{reservationId ?? 0}";

        return await WithChatLockAsync(
            resource,
            async () =>
            {
                string? reservationCode = null;

                if (reservationId.HasValue)
                {
                    reservationCode = await _context
                        .Set<Reservation>()
                        .AsNoTracking()
                        .Where(reservation =>
                            reservation.Id == reservationId.Value
                            && reservation.CustomerId == actor.Id)
                        .Select(reservation => reservation.Code)
                        .SingleOrDefaultAsync(cancellationToken);

                    if (reservationCode == null)
                    {
                        throw new ChatException(
                            404,
                            "Không tìm thấy lượt đặt bàn.");
                    }
                }

                var query = _context.Set<ChatConversation>()
                    .AsTracking()
                    .Where(conversation =>
                        conversation.CustomerId == actor.Id);

                if (reservationId.HasValue)
                {
                    // Hội thoại đặt bàn được giữ lại
                    // kể cả khi đã đóng để xem lịch sử.
                    query = query.Where(conversation =>
                        conversation.ReservationId ==
                            reservationId.Value);
                }
                else
                {
                    query = query.Where(conversation =>
                        conversation.ReservationId == null
                        && conversation.Status == "OPEN");
                }

                var existing = await query
                    .SingleOrDefaultAsync(cancellationToken);

                if (existing != null)
                {
                    return existing;
                }

                var now = DateTime.UtcNow;

                var conversation = new ChatConversation
                {
                    CustomerId = actor.Id,
                    ReservationId = reservationId,

                    Title = reservationCode == null
                        ? "Tư vấn cùng DoraFood"
                        : $"Tư vấn đặt bàn {reservationCode}",

                    Status = "OPEN",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                _context.Set<ChatConversation>().Add(conversation);

                await _context.SaveChangesAsync(cancellationToken);

                return conversation;
            },
            cancellationToken);
    }

    // Controller khách truyền fromStaff: false.
    // Controller quản trị truyền fromStaff: true.
    // Không nhận giá trị này từ form của trình duyệt.
    public async Task<ChatMessage> SendAsync(
        ClaimsPrincipal user,
        long conversationId,
        string? body,
        Guid clientMessageId,
        bool fromStaff,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(
            user,
            cancellationToken);

        if (fromStaff && !actor.IsStaff)
        {
            throw new ChatException(
                403,
                "Tài khoản không có quyền trả lời phía nhà hàng.");
        }

        body = body?.Trim();

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ChatException(
                400,
                "Anh hãy nhập nội dung tin nhắn.");
        }

        if (body.Length > 2000)
        {
            throw new ChatException(
                400,
                "Tin nhắn tối đa 2.000 ký tự.");
        }

        if (conversationId <= 0
            || clientMessageId == Guid.Empty)
        {
            throw new ChatException(
                400,
                "Thông tin gửi tin nhắn không hợp lệ.");
        }

        var senderType = fromStaff ? "STAFF" : "CUSTOMER";

        return await WithChatLockAsync(
            $"DoraFood.Chat.Conversation:{conversationId}",
            async () =>
            {
                var conversation = await _context
                    .Set<ChatConversation>()
                    .AsTracking()
                    .SingleOrDefaultAsync(
                        item =>
                            item.Id == conversationId
                            && (
                                fromStaff
                                || item.CustomerId == actor.Id),
                        cancellationToken);

                if (conversation == null)
                {
                    throw new ChatException(
                        404,
                        "Không tìm thấy hội thoại.");
                }

                // Gửi lại cùng yêu cầu thì trả tin đã có,
                // không tạo thêm tin hoặc thông báo thứ hai.
                var existing = await _context.Set<ChatMessage>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        message =>
                            message.ConversationId == conversationId
                            && message.SenderId == actor.Id
                            && message.ClientMessageId ==
                                clientMessageId,
                        cancellationToken);

                if (existing != null)
                {
                    if (existing.Body != body
                        || existing.SenderType != senderType)
                    {
                        throw new ChatException(
                            409,
                            "Mã gửi tin đã được dùng cho nội dung khác.");
                    }

                    return existing;
                }

                if (conversation.Status != "OPEN")
                {
                    throw new ChatException(
                        409,
                        "Hội thoại đã đóng, chưa thể gửi thêm tin.");
                }

                var now = DateTime.UtcNow;

                var message = new ChatMessage
                {
                    ConversationId = conversation.Id,
                    SenderId = actor.Id,
                    SenderType = senderType,
                    Body = body,
                    ClientMessageId = clientMessageId,
                    CreatedAtUtc = now,
                    ReadAtUtc = null
                };

                _context.Set<ChatMessage>().Add(message);

                conversation.UpdatedAtUtc = now;

                if (fromStaff)
                {
                    var preview = body.Length > 160
                        ? body[..160] + "..."
                        : body;

                    _notifications.Add(
                        conversation.CustomerId,
                        "DoraFood đã trả lời tin nhắn",
                        preview,
                        $"/tro-chuyen/{conversation.Id}");
                }

                // Tin nhắn, thời gian cập nhật hội thoại
                // và thông báo được lưu cùng nhau.
                await _context.SaveChangesAsync(
                    cancellationToken);

                return message;
            },
            cancellationToken);
    }

    // Kiểm tra tài khoản thực tế và phiên đăng nhập.
    public async Task<(long Id, bool IsStaff)> GetActorAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !long.TryParse(
                principal.FindFirstValue(
                    ClaimTypes.NameIdentifier),
                out var userId)
            || userId <= 0
            || !Guid.TryParse(
                principal.FindFirstValue("SecurityStamp"),
                out var securityStamp))
        {
            throw new ChatException(
                401,
                "Anh cần đăng nhập để nhắn tin.");
        }

        var account = await _context.Set<User>()
            .AsNoTracking()
            .Where(user =>
                user.Id == userId
                && user.IsActive
                && user.SecurityStamp == securityStamp)
            .Select(user => new
            {
                user.Id,

                IsStaff = user.Roles.Any(role =>
                    role.Code == "ADMIN"
                    || role.Code == "MANAGER")
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (account == null)
        {
            throw new ChatException(
                401,
                "Phiên đăng nhập không còn hợp lệ.");
        }

        return (account.Id, account.IsStaff);
    }

    // Mỗi hội thoại được xử lý lần lượt trong giao dịch.
    private async Task<T> WithChatLockAsync<T>(
        string resource,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;

            IF @LockResult < 0
                THROW 56070,
                    N'Hội thoại đang bận. Vui lòng thử lại.',
                    1;
            """,
            cancellationToken);

        var result = await action();

        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}

// Lỗi nghiệp vụ để controller trả thông báo phù hợp.
public sealed class ChatException : Exception
{
    public int StatusCode { get; }

    public ChatException(
        int statusCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
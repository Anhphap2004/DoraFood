using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Services;

public sealed class ChatService
{
    private readonly DoraFoodContext _context;
    private readonly NotificationService _notifications;
    private readonly ChatImageService _images;
    private readonly string _imageDirectory;

    public ChatService(
        DoraFoodContext context,
        NotificationService notifications,
        ChatImageService images,
        IWebHostEnvironment environment)
    {
        _context = context;
        _notifications = notifications;
        _images = images;

        _imageDirectory = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "ChatImages");
    }
    // Mở hội thoại của khách.
    // reservationId = null: tư vấn chung.
    // Có reservationId: tư vấn về lượt đặt bàn đó.
    public async Task<ChatConversation> OpenConversationAsync(
    ClaimsPrincipal user,
    long? reservationId,
    CancellationToken cancellationToken,
    bool fromStaff = false)
    {
        var actor = await GetActorAsync(user, cancellationToken);

        if (fromStaff && !actor.IsStaff)
        {
            throw new ChatException(403, "Tài khoản không có quyền tư vấn đặt bàn.");
        }

        if (reservationId is <= 0 || (fromStaff && !reservationId.HasValue))
        {
            throw new ChatException(400, "Thông tin đặt bàn không hợp lệ.");
        }

        var resource = reservationId.HasValue
            ? $"DoraFood.Chat.OpenReservation:{reservationId.Value}"
            : $"DoraFood.Chat.Open:{actor.Id}:0";

        return await WithChatLockAsync(resource, async () =>
        {
            var conversations = _context.Set<ChatConversation>().AsTracking();
            var customerId = actor.Id;
            var title = "Tư vấn cùng DoraFood";
            ChatConversation? existing;

            if (reservationId is long id)
            {
                var booking = await _context.Set<Reservation>()
                    .AsNoTracking()
                    .Where(item => item.Id == id && (fromStaff || item.CustomerId == actor.Id))
                    .Select(item => new
                    {
                        item.CustomerId,
                        item.Code,
                        item.Status,
                        item.StartsAtUtc,
                        HasTables = item.Tables.Any()
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                if (booking == null || booking.CustomerId is not long ownerId)
                {
                    throw new ChatException(404, "Không tìm thấy lượt đặt bàn gắn với tài khoản khách.");
                }

                customerId = ownerId;
                existing = await conversations.SingleOrDefaultAsync(
                    item => item.ReservationId == id && item.CustomerId == customerId,
                    cancellationToken);

                if (existing != null)
                {
                    return existing;
                }

                if (booking.Status != "CONFIRMED"
                    || !booking.HasTables
                    || booking.StartsAtUtc <= DateTime.UtcNow.AddMinutes(-30))
                {
                    throw new ChatException(409, "Lượt đặt cần được xác nhận, đã phân bàn và còn hạn giữ bàn để mở tư vấn món.");
                }

                title = $"Tư vấn đặt bàn {booking.Code}";
            }
            else
            {
                existing = await conversations.SingleOrDefaultAsync(
                    item => item.CustomerId == customerId
                        && item.ReservationId == null
                        && item.Status == "OPEN",
                    cancellationToken);

                if (existing != null)
                {
                    return existing;
                }
            }

            var now = DateTime.UtcNow;
            var conversation = new ChatConversation
            {
                CustomerId = customerId,
                ReservationId = reservationId,
                Title = title,
                Status = "OPEN",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _context.Set<ChatConversation>().Add(conversation);
            await _context.SaveChangesAsync(cancellationToken);
            return conversation;
        }, cancellationToken);
    }

    // Controller khách truyền fromStaff: false.
    // Controller quản trị truyền fromStaff: true.
    // Không nhận giá trị này từ form của trình duyệt.
    // Controller khách truyền fromStaff: false.
    // Controller Admin truyền fromStaff: true.
    // Không lấy fromStaff từ dữ liệu trình duyệt gửi lên.
    public async Task<ChatMessage> SendAsync(
        ClaimsPrincipal user,
        long conversationId,
        string? body,
        Guid clientMessageId,
        bool fromStaff,
        CancellationToken cancellationToken,
        IFormFile? image = null)
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

        body = body?.Trim() ?? string.Empty;

        if (body.Length == 0 && image == null)
        {
            throw new ChatException(
                400,
                "Anh hãy nhập tin nhắn hoặc chọn một ảnh.");
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

        // Kiểm tra quyền trước khi thực hiện xử lý ảnh.
        var canAccess = await _context.Set<ChatConversation>()
            .AsNoTracking()
            .AnyAsync(
                conversation =>
                    conversation.Id == conversationId
                    && (
                        fromStaff
                        || conversation.CustomerId == actor.Id),
                cancellationToken);

        if (!canAccess)
        {
            throw new ChatException(
                404,
                "Không tìm thấy hội thoại.");
        }

        PreparedChatImage? preparedImage = null;
        string? imageFileName = null;
        string? imageStorageKey = null;

        if (image != null)
        {
            // Giải mã và chuẩn hóa ảnh ngoài giao dịch SQL,
            // tránh giữ khóa hội thoại trong lúc xử lý ảnh.
            preparedImage = await _images.PrepareAsync(
                image,
                cancellationToken);

            // Tên do server tạo.
            // Cùng yêu cầu gửi và cùng nội dung ảnh sẽ có cùng tên.
            imageFileName =
                $"{conversationId}-{actor.Id}-"
                + $"{clientMessageId:N}-"
                + $"{preparedImage.SourceHash}.png";

            // Đây là khóa lưu trữ nội bộ,
            // không phải URL công khai để truy cập trực tiếp.
            imageStorageKey = $"chat-private/{imageFileName}";
        }

        var senderType = fromStaff ? "STAFF" : "CUSTOMER";

        return await WithChatLockAsync(
            $"DoraFood.Chat.Conversation:{conversationId}",
            async () =>
            {
                // Kiểm tra lại quyền trong giao dịch.
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

                // Kiểm tra yêu cầu đã được xử lý trước đó chưa.
                var existing = await _context.Set<ChatMessage>()
                    .AsNoTracking()
                    .Include(message => message.MediaFile)
                    .SingleOrDefaultAsync(
                        message =>
                            message.ConversationId == conversationId
                            && message.SenderId == actor.Id
                            && message.ClientMessageId == clientMessageId,
                        cancellationToken);

                if (existing != null)
                {
                    var sameImage = imageStorageKey == null
                        ? existing.MediaFileId == null
                        : existing.MediaFile != null
                            && existing.MediaFile.Url == imageStorageKey;

                    if (existing.Body != body
                        || existing.SenderType != senderType
                        || !sameImage)
                    {
                        throw new ChatException(
                            409,
                            "Mã gửi tin đã được dùng cho nội dung khác.");
                    }

                    // Trả về tin đã có, không tạo trùng tin,
                    // ảnh trong SQL hoặc thông báo.
                    return existing;
                }

                if (conversation.Status != "OPEN")
                {
                    throw new ChatException(
                        409,
                        "Hội thoại đã đóng, chưa thể gửi thêm tin.");
                }

                var now = DateTime.UtcNow;

                MediaFile? media = null;

                if (preparedImage != null)
                {
                    await SaveChatImageAsync(
                        preparedImage,
                        imageFileName!,
                        cancellationToken);

                    media = new MediaFile
                    {
                        Url = imageStorageKey!,
                        OriginalName = preparedImage.OriginalName,
                        MimeType = preparedImage.MimeType,
                        SizeBytes = preparedImage.SizeBytes,
                        AltText = "Ảnh đính kèm tin nhắn",
                        UploadedBy = actor.Id,
                        CreatedAtUtc = now
                    };

                    _context.Set<MediaFile>().Add(media);
                }

                var message = new ChatMessage
                {
                    ConversationId = conversation.Id,
                    SenderId = actor.Id,
                    SenderType = senderType,
                    Body = body,

                    // EF tự lấy ID ảnh sau khi thêm MediaFile.
                    MediaFile = media,

                    ClientMessageId = clientMessageId,
                    CreatedAtUtc = now,
                    ReadAtUtc = null
                };

                _context.Set<ChatMessage>().Add(message);

                conversation.UpdatedAtUtc = now;

                if (fromStaff)
                {
                    var preview = body.Length == 0
                        ? "Nhà hàng đã gửi một hình ảnh."
                        : body.Length > 160
                            ? body[..160] + "..."
                            : body;

                    _notifications.Add(
                        conversation.CustomerId,
                        "DoraFood đã trả lời tin nhắn",
                        preview,
                        $"/tro-chuyen/{conversation.Id}");
                }

                // MediaFile, tin nhắn, thời gian hội thoại
                // và thông báo được lưu trong cùng giao dịch SQL.
                await _context.SaveChangesAsync(
                    cancellationToken);

                return message;
            },
            cancellationToken);
    }
    private async Task SaveChatImageAsync(
    PreparedChatImage image,
    string fileName,
    CancellationToken cancellationToken)
    {
        string? temporaryPath = null;

        try
        {
            Directory.CreateDirectory(_imageDirectory);

            var destinationPath = Path.Combine(
                _imageDirectory,
                fileName);

            temporaryPath = Path.Combine(
                _imageDirectory,
                $"{Guid.NewGuid():N}.tmp");

            // Ghi vào file tạm trước.
            await File.WriteAllBytesAsync(
                temporaryPath,
                image.Bytes,
                cancellationToken);

            // Chỉ đưa vào tên chính thức khi đã ghi xong.
            // Tên ảnh được tạo hoàn toàn bởi server.
            File.Move(
                temporaryPath,
                destinationPath,
                overwrite: true);
        }
        catch (IOException)
        {
            throw new ChatException(
                503,
                "Chưa lưu được ảnh. Anh hãy thử gửi lại.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ChatException(
                503,
                "Hệ thống chưa có quyền ghi thư mục ảnh chat.");
        }
        finally
        {
            // Dọn file tạm nếu việc ghi hoặc di chuyển bị lỗi.
            if (temporaryPath != null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Không để lỗi dọn file tạm che mất lỗi chính.
                }
                catch (UnauthorizedAccessException)
                {
                    // Không để lỗi dọn file tạm che mất lỗi chính.
                }
            }
        }
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
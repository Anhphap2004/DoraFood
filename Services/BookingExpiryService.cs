using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DoraFood.Services;

public sealed class BookingExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingExpiryService> _logger;

    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromSeconds(30);

    private const int ArrivalGraceMinutes = 30;
    private const int BatchSize = 100;

    // Tránh ghi cùng cảnh báo liên tục mỗi 30 giây.
    private DateTime _nextWarningAtUtc = DateTime.MinValue;

    public BookingExpiryService(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cancelledCount =
                    await CancelExpiredBookingsAsync(stoppingToken);

                if (cancelledCount > 0)
                {
                    _logger.LogInformation(
                        "Đã tự động hủy {Count} lượt đặt bàn quá hạn.",
                        cancelledCount);
                }

                // Xử lý tiếp nếu vừa hủy đủ một đợt.
                if (cancelledCount == BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Lỗi khi tự hủy đặt bàn quá hạn. " +
                    "Hệ thống sẽ thử lại ở lần kiểm tra tiếp theo.");
            }

            try
            {
                await Task.Delay(
                    CheckInterval,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<int> CancelExpiredBookingsAsync(
        CancellationToken cancellationToken)
    {
        // DbContext và NotificationService dùng chung scope.
        await using var scope =
            _scopeFactory.CreateAsyncScope();

        var context = scope.ServiceProvider
            .GetRequiredService<DoraFoodContext>();

        var notifications = scope.ServiceProvider
            .GetRequiredService<NotificationService>();

        await using var transaction =
            await context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        // Dùng cùng khóa với các thao tác quản lý đặt bàn.
        await context.Database.ExecuteSqlRawAsync(
            """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'DoraFood.Reservations',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;

            IF @LockResult < 0
                THROW 56028,
                    N'Chưa thể khóa dữ liệu để kiểm tra hết hạn.',
                    1;
            """,
            cancellationToken);

        var now = DateTime.UtcNow;

        var latestExpiredStart =
            now.AddMinutes(-ArrivalGraceMinutes);

        var expiredQuery = context.Set<Reservation>()
            .Where(reservation =>
                (
                    reservation.Status == "PENDING"
                    || reservation.Status == "CONFIRMED")
                && reservation.StartsAtUtc <= latestExpiredStart);

        // Các lượt phải để quản lý kiểm tra:
        // có hóa đơn, đã phục vụ, đã gắn bàn vào đơn
        // hoặc có khoản thanh toán cần đối soát.
        var blockedReservationIds = context.Set<Order>()
            .Where(order =>
                order.ReservationId.HasValue
                && order.OrderType == "DINE_IN"
                && (
                    order.Invoice != null
                    || order.Status == "COMPLETED"
                    || order.OrderTables.Any()
                    || order.OrderItems.Any(
                        item => item.Status == "SERVED")
                    || order.Payments.Any(
                        payment =>
                            payment.Status == "PENDING"
                            || payment.Status == "SUCCEEDED")
                    || (
                        (
                            order.Status == "OPEN"
                            || order.Status == "IN_PROGRESS")
                        && order.ClosedAtUtc.HasValue)))
            .Select(order => order.ReservationId!.Value);

        // Chỉ ghi cảnh báo định kỳ, không hủy các lượt này.
        if (now >= _nextWarningAtUtc)
        {
            var blockedCodes = await expiredQuery
                .AsNoTracking()
                .Where(reservation =>
                    blockedReservationIds.Contains(reservation.Id))
                .OrderBy(reservation => reservation.StartsAtUtc)
                .ThenBy(reservation => reservation.Id)
                .Select(reservation => reservation.Code)
                .Take(5)
                .ToListAsync(cancellationToken);

            if (blockedCodes.Count > 0)
            {
                _logger.LogWarning(
                    "Các lượt đặt bàn quá hạn cần kiểm tra thủ công: " +
                    "{BookingCodes}. Có dữ liệu phục vụ, hóa đơn " +
                    "hoặc khoản thanh toán nên chưa tự hủy.",
                    string.Join(", ", blockedCodes));
            }

            _nextWarningAtUtc = now.AddMinutes(5);
        }

        // Loại các lượt bị chặn trước khi phân đợt.
        // Tránh một số lượt bị chặn làm các lượt phía sau
        // không bao giờ được xử lý.
        var expiredBookings = await expiredQuery
            .AsTracking()
            .Where(reservation =>
                !blockedReservationIds.Contains(reservation.Id))
            .OrderBy(reservation => reservation.StartsAtUtc)
            .ThenBy(reservation => reservation.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (expiredBookings.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var reservationIds = expiredBookings
            .Select(reservation => reservation.Id)
            .ToArray();

        // Đọc các đơn món đặt trước trong cả đợt bằng một truy vấn.
        var preorderOrders = await context.Set<Order>()
            .AsTracking()
            .Include(order => order.OrderItems)
            .Where(order =>
                order.ReservationId.HasValue
                && reservationIds.Contains(
                    order.ReservationId.Value)
                && order.OrderType == "DINE_IN"
                && (
                    order.Status == "OPEN"
                    || order.Status == "IN_PROGRESS"))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var ordersByReservation = preorderOrders
            .ToLookup(order => order.ReservationId!.Value);

        var correlationId = Guid.NewGuid();

        foreach (var reservation in expiredBookings)
        {
            var previousStatus = reservation.Status;

            var relatedOrders =
                ordersByReservation[reservation.Id].ToList();

            // Hủy các đơn món chưa phục vụ.
            foreach (var order in relatedOrders)
            {
                order.Status = "CANCELLED";
                order.ClosedAtUtc = now;

                foreach (var item in order.OrderItems
                    .Where(item => item.Status != "CANCELLED"))
                {
                    item.Status = "CANCELLED";
                }

                // Giữ nguyên số lượng, đơn giá, ghi chú
                // và các dòng món thêm để tra cứu.
                context.Set<AuditLog>().Add(new AuditLog
                {
                    ActorId = null,
                    Action = "TABLE_PREORDER_CANCELLED",
                    EntityName = "Order",

                    EntityKey = order.Id.ToString(
                        CultureInfo.InvariantCulture),

                    Summary =
                        $"Hệ thống hủy đơn món đặt trước {order.Code} " +
                        $"do lượt đặt bàn {reservation.Code} " +
                        "quá hạn nhận khách 30 phút.",

                    CorrelationId = correlationId,
                    CreatedAtUtc = now
                });
            }

            reservation.Status = "CANCELLED";

            context.Set<AuditLog>().Add(new AuditLog
            {
                ActorId = null,
                Action = "BOOKING_CANCELLED",
                EntityName = "Reservation",

                EntityKey = reservation.Id.ToString(
                    CultureInfo.InvariantCulture),

                Summary =
                    "Hệ thống tự động hủy từ trạng thái "
                    + previousStatus
                    + " vì chưa nhận khách sau 30 phút "
                    + "kể từ giờ hẹn. "
                    + $"Đã hủy {relatedOrders.Count} đơn món đặt trước.",

                CorrelationId = correlationId,
                CreatedAtUtc = now
            });

            var arrivalText = reservation.StartsAtUtc
                .AddHours(7)
                .ToString(
                    "dd/MM/yyyy HH:mm",
                    CultureInfo.GetCultureInfo("vi-VN"));

            var body =
                $"Yêu cầu {reservation.Code}, " +
                $"giờ hẹn {arrivalText}, đã tự động hủy " +
                "do nhà hàng chưa ghi nhận khách vào bàn " +
                "sau thời gian giữ bàn 30 phút.";

            if (relatedOrders.Count > 0)
            {
                body +=
                    " Các đơn món đặt trước chưa phục vụ " +
                    "thuộc lượt đặt bàn này cũng đã được hủy.";
            }

            body +=
                " Quý khách vui lòng liên hệ nhà hàng " +
                "nếu cần hỗ trợ đặt lại.";

            notifications.Add(
                reservation.CustomerId,
                "Đặt bàn đã hủy do quá hạn nhận khách",
                body,
                $"/dat-ban/cua-toi/{reservation.Id}");

            // Không xóa liên kết bàn đã phân:
            // giữ lại thông tin lịch sử đặt bàn.
        }

        // Lưu trạng thái đặt bàn, đơn món, các món,
        // lịch sử và thông báo trong cùng giao dịch.
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return expiredBookings.Count;
    }
}
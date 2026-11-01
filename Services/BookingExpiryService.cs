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

namespace DoraFood.Services
{
    public sealed class BookingExpiryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookingExpiryService> _logger;

        private static readonly TimeSpan CheckInterval =
            TimeSpan.FromSeconds(30);

        private const int ArrivalGraceMinutes = 30;
        private const int BatchSize = 100;

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
                            "Đã tự động hủy {Count} yêu cầu đặt bàn quá hạn.",
                            cancelledCount);
                    }

                    // Nếu còn nhiều yêu cầu quá hạn,
                    // tiếp tục xử lý đợt tiếp theo.
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
                        "Lỗi khi tự động hủy đặt bàn quá hạn. "
                        + "Hệ thống sẽ thử lại ở lần kiểm tra tiếp theo.");
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
            // Mỗi lần kiểm tra dùng một DbContext riêng.
            await using var scope =
                _scopeFactory.CreateAsyncScope();

            var context = scope.ServiceProvider
                .GetRequiredService<DoraFoodContext>();

            await using var transaction =
                await context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    cancellationToken);

            // Cùng khóa với xác nhận, nhận khách và điều chỉnh.
            // Tránh tác vụ hủy chạy đồng thời với nhận khách.
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

            var expiredBookings = await context.Set<Reservation>()
                .AsTracking()
                .Where(r =>
                    (r.Status == "PENDING"
                        || r.Status == "CONFIRMED")
                    && r.StartsAtUtc <= latestExpiredStart)
                .OrderBy(r => r.StartsAtUtc)
                .ThenBy(r => r.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (expiredBookings.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return 0;
            }

            // Các bản ghi trong cùng đợt có chung mã đối chiếu.
            var correlationId = Guid.NewGuid();

            foreach (var reservation in expiredBookings)
            {
                var previousStatus = reservation.Status;

                reservation.Status = "CANCELLED";

                context.Set<AuditLog>().Add(new AuditLog
                {
                    // Không có nhân viên thao tác.
                    ActorId = null,

                    Action = "BOOKING_CANCELLED",
                    EntityName = "Reservation",

                    EntityKey = reservation.Id.ToString(
                        CultureInfo.InvariantCulture),

                    Summary =
                        "Hệ thống tự động hủy từ trạng thái "
                        + previousStatus
                        + " vì chưa nhận khách sau 30 phút "
                        + "kể từ giờ hẹn.",

                    CorrelationId = correlationId,
                    CreatedAtUtc = now
                });

                // Giữ nguyên liên kết bàn để xem lịch sử.
            }

            // Trạng thái và lịch sử được lưu cùng giao dịch.
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return expiredBookings.Count;
        }
    }
}
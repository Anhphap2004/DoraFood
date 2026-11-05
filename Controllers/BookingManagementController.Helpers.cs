using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Controllers
{
    public partial class BookingManagementController
    {
        // Mọi thao tác thay đổi lịch dùng cùng khóa với BookingExpiryService.
        // Action tự SaveChanges khi hợp lệ; hàm này chỉ quản lý giao dịch và lỗi.
        private async Task<IActionResult> RunBookingTransactionAsync(
            Func<Task<IActionResult>> action,
            Func<IActionResult> onError,
            string errorKey)
        {
            var cancellationToken = HttpContext.RequestAborted;
            try
            {
                await using var transaction = await _context.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                await _context.Database.ExecuteSqlRawAsync(
                    """
                    DECLARE @LockResult int;
                    EXEC @LockResult = sys.sp_getapplock
                        @Resource = N'DoraFood.Reservations',
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction',
                        @LockTimeout = 10000;
                    IF @LockResult < 0
                        THROW 56030, N'Không thể khóa dữ liệu đặt bàn.', 1;
                    """, cancellationToken);

                var result = await action();
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Xung đột phiên bản tại {Path}", Request.Path);
                TempData[errorKey] = "Dữ liệu đã thay đổi. Vui lòng tải lại trang và kiểm tra.";
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Lỗi SQL tại {Path}", Request.Path);
                TempData[errorKey] = "Chưa thể lưu thao tác. Vui lòng tải lại và kiểm tra dữ liệu.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Lỗi lưu dữ liệu tại {Path}", Request.Path);
                TempData[errorKey] = "Không lưu được do dữ liệu liên quan đã thay đổi. Vui lòng kiểm tra lại.";
            }

            // Không hiển thị thành công nếu giao dịch không hoàn tất.
            TempData.Remove("BookingManagementSuccess");
            TempData.Remove("TableOrderSuccess");
            return onError();
        }

        private static bool IsCurrentVersion(Reservation reservation, string? version)
        {
            if (string.IsNullOrWhiteSpace(version) || version.Length > 64)
                return false;

            try
            {
                return Convert.FromBase64String(version).SequenceEqual(reservation.Version);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Lịch tương lai xét đúng ngày/buổi; khách đang ngồi chỉ chặn thêm
        // buổi hiện tại. Khi thực nhận khách, Seat kiểm tra bàn đang dùng lần nữa.
        private IQueryable<Reservation> BlockingReservations(
            long excludedId, DateTime sessionStartUtc, DateTime sessionEndUtc)
        {
            var now = DateTime.UtcNow;
            var isCurrentSession = now >= sessionStartUtc && now < sessionEndUtc;
            return _context.Set<Reservation>().AsNoTracking().Where(r =>
                r.Id != excludedId
                && (((r.Status == "PENDING" || r.Status == "CONFIRMED" || r.Status == "SEATED")
                        && r.StartsAtUtc >= sessionStartUtc && r.StartsAtUtc < sessionEndUtc)
                    || (r.Status == "SEATED" && isCurrentSession)));
        }

        private async Task<List<DiningTable>?> ValidateTablesAsync(
            long reservationId, long[] tableIds, short partySize,
            DateTime sessionStartUtc, DateTime sessionEndUtc, bool required)
        {
            string? error = null;
            var tables = new List<DiningTable>();
            var cancellationToken = HttpContext.RequestAborted;

            if (tableIds.Length > 200 || tableIds.Any(id => id <= 0))
                error = "Danh sách bàn không hợp lệ.";
            else if (required && tableIds.Length == 0)
                error = "Vui lòng chọn ít nhất một bàn.";
            else if (tableIds.Length > 0)
            {
                tables = await _context.Set<DiningTable>().AsTracking()
                    .Where(t => tableIds.Contains(t.Id) && t.IsActive && t.Area.IsActive)
                    .ToListAsync(cancellationToken);

                if (tables.Count != tableIds.Length)
                    error = "Có bàn không tồn tại hoặc bàn/khu vực đã ngừng hoạt động.";
                else if (tables.Sum(t => (int)t.Capacity) < partySize)
                    error = "Tổng sức chứa của các bàn không đủ cho số khách.";
                else if (await BlockingReservations(reservationId, sessionStartUtc, sessionEndUtc)
                    .AnyAsync(r => r.Tables.Any(t => tableIds.Contains(t.Id)), cancellationToken))
                    error = "Có bàn đã được giữ trong buổi này hoặc đang phục vụ khách. Vui lòng chọn lại.";
            }

            if (error == null) return tables;
            ModelState.AddModelError("TableIds", error);
            return null;
        }

        private void AddBookingHistory(
            Reservation reservation,
            string action,
            string summary)
        {
            var actorIdText = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (!long.TryParse(actorIdText, out var actorId))
            {
                throw new InvalidOperationException(
                    "Không xác định được tài khoản thực hiện thao tác.");
            }

            var log = new AuditLog
            {
                ActorId = actorId,
                Action = action,
                EntityName = "Reservation",

                EntityKey = reservation.Id.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),

                Summary = summary,
                CorrelationId = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Set<AuditLog>().Add(log);
        }

        private async Task LoadEditBookingDataAsync(
            EditBookingViewModel model,
            Reservation reservation)
        {
            model.Id = reservation.Id;
            model.Reservation = reservation;

            // Đây là danh sách bàn đang hoạt động.
            // Lịch trùng sẽ được kiểm tra khi xử lý lưu thay đổi.
            model.Tables = await _context.Set<DiningTable>()
                .AsNoTracking()
                .Include(t => t.Area)
                .Where(t => t.IsActive && t.Area.IsActive)
                .OrderBy(t => t.Area.Name)
                .ThenBy(t => t.Code)
                .ThenBy(t => t.Id)
                .ToListAsync(HttpContext.RequestAborted);

            ViewData["Title"] =
                $"Điều chỉnh đặt bàn {reservation.Code}";
        }

        private async Task<ConfirmBookingViewModel> BuildConfirmModelAsync(
            Reservation reservation,
            ConfirmBookingViewModel? model = null)
        {
            if (model == null)
            {
                model = new ConfirmBookingViewModel
                {
                    TableIds = reservation.Tables
                        .Select(t => t.Id)
                        .ToList()
                };
            }

            model.Id = reservation.Id;
            model.Version = Convert.ToBase64String(reservation.Version);
            model.Reservation = reservation;

            var reservationId = reservation.Id;

            ViewData["Title"] = "Chọn bàn và xác nhận";

            if (!TryGetServiceSessionBounds(
                    reservation.StartsAtUtc,
                    out var sessionStartUtc,
                    out var sessionEndUtc))
            {
                model.Tables = new List<BookingTableOption>();
                ModelState.AddModelError(
                    string.Empty,
                    "Giờ đến nằm ngoài khung phục vụ. Vui lòng kiểm tra lại.");
                return model;
            }

            var blockedTableIds = BlockingReservations(
                    reservationId, sessionStartUtc, sessionEndUtc)
                .SelectMany(item => item.Tables)
                .Select(table => table.Id);

            model.Tables = await _context.Set<DiningTable>()
                .AsNoTracking()

                // Chỉ đưa ra các bàn và khu vực đang hoạt động.
                .Where(t => t.IsActive && t.Area.IsActive)

                .OrderBy(t => t.Area.Name)
                .ThenBy(t => t.Code)
                .ThenBy(t => t.Id)

                .Select(t => new BookingTableOption
                {
                    Id = t.Id,
                    Code = t.Code,
                    AreaName = t.Area.Name,
                    Capacity = t.Capacity,

                    IsAvailable = !blockedTableIds.Contains(t.Id)
                })
                .ToListAsync(HttpContext.RequestAborted);

            ViewData["Title"] = "Chọn bàn và xác nhận";

            return model;
        }

        private static bool TryGetServiceSessionBounds(
            DateTime startsAtUtc,
            out DateTime sessionStartUtc,
            out DateTime sessionEndUtc)
        {
            sessionStartUtc = default;
            sessionEndUtc = default;

            try
            {
                var local = DateTime.SpecifyKind(
                    startsAtUtc.AddHours(7), DateTimeKind.Unspecified);

                var time = local.TimeOfDay;
                var isLunch = time >= TimeSpan.FromHours(10)
                    && time < TimeSpan.FromHours(14);
                var isDinner = time >= TimeSpan.FromHours(16)
                    && time < TimeSpan.FromHours(22);

                if (!isLunch && !isDinner)
                {
                    return false;
                }

                var offset = TimeSpan.FromHours(7);

                sessionStartUtc = new DateTimeOffset(
                    local.Date.AddHours(isLunch ? 10 : 16), offset).UtcDateTime;

                sessionEndUtc = new DateTimeOffset(
                    local.Date.AddHours(isLunch ? 14 : 22), offset).UtcDateTime;

                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private async Task RedirectToLoginAsync(
            ActionExecutingContext context)
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

            context.Result = RedirectToAction(
                "Login",
                "Account",
                new
                {
                    returnUrl = Url.RouteUrl(
                        "DoraFoodManageBookings")
                });
        }
    }
}

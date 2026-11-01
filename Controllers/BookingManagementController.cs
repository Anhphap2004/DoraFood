using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Controllers
{
    [Authorize(Roles = "ADMIN,MANAGER")]
    public class BookingManagementController : Controller
    {
        private readonly DoraFoodContext _context;
        private readonly ILogger<BookingManagementController> _logger;

        public BookingManagementController(
            DoraFoodContext context,
            ILogger<BookingManagementController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private static readonly string[] AllowedStatuses =
        {
            "ALL",
            "PENDING",
            "CONFIRMED",
            "SEATED",
            "COMPLETED",
            "CANCELLED",
            "NO_SHOW"
        };
        // Chuẩn bị bản ghi lịch sử.
        // Dữ liệu sẽ được lưu khi action gọi SaveChangesAsync.
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
        public class EditBookingViewModel
        {
            [BindNever]
            public long Id { get; set; }

            [Required(ErrorMessage = "Thiếu phiên bản dữ liệu.")]
            [StringLength(64)]
            public string Version { get; set; } = string.Empty;

            [Required(ErrorMessage = "Vui lòng nhập số khách.")]
            [Range(
                1,
                short.MaxValue,
                ErrorMessage = "Số khách phải từ 1 đến 32767.")]
            public short? PartySize { get; set; }

            [Required(ErrorMessage = "Vui lòng chọn giờ bắt đầu.")]
            [DataType(DataType.DateTime)]
            public DateTime? StartsAtLocal { get; set; }

            // Tự tính cuối buổi phục vụ; không nhận từ form.
            [BindNever]
            [ValidateNever]
            public DateTime? EndsAtLocal { get; set; }

            [MaxLength(
                200,
                ErrorMessage = "Số lượng bàn được chọn vượt giới hạn.")]
            public List<long> TableIds { get; set; } = new();

            [Required(ErrorMessage = "Vui lòng nhập lý do điều chỉnh.")]
            [StringLength(
                300,
                ErrorMessage = "Lý do điều chỉnh tối đa 300 ký tự.")]
            public string Reason { get; set; } = string.Empty;

            [BindNever]
            [ValidateNever]
            public Reservation Reservation { get; set; } = null!;

            [BindNever]
            [ValidateNever]
            public List<DiningTable> Tables { get; set; } = new();
        }
        // GET: /quan-ly/dat-ban/5/dieu-chinh
        [HttpGet(
            "/quan-ly/dat-ban/{id:long}/dieu-chinh",
            Name = "DoraFoodEditBooking")]
        public async Task<IActionResult> Edit([FromRoute] long id)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            var cancellationToken = HttpContext.RequestAborted;

            var reservation = await _context.Set<Reservation>()
                .AsNoTracking()
                .Include(r => r.Tables)
                .FirstOrDefaultAsync(
                    r => r.Id == id,
                    cancellationToken);

            if (reservation == null)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            if (reservation.Status != "PENDING"
                && reservation.Status != "CONFIRMED")
            {
                TempData["BookingManagementError"] =
                    "Chỉ được điều chỉnh yêu cầu đang chờ hoặc đã xác nhận.";

                return RedirectToRoute(
                    "DoraFoodManageBookingDetails",
                    new { id });
            }

            var model = new EditBookingViewModel
            {
                Id = reservation.Id,

                Version = Convert.ToBase64String(
                    reservation.Version),

                PartySize = reservation.PartySize,

                // Đổi UTC sang giờ Việt Nam để hiển thị.
                StartsAtLocal = DateTime.SpecifyKind(
                    reservation.StartsAtUtc.AddHours(7),
                    DateTimeKind.Unspecified),

                EndsAtLocal = DateTime.SpecifyKind(
                    reservation.EndsAtUtc.AddHours(7),
                    DateTimeKind.Unspecified),

                TableIds = reservation.Tables
                    .Select(t => t.Id)
                    .ToList()
            };

            await LoadEditBookingDataAsync(model, reservation);

            return View(model);
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
        // POST: /quan-ly/dat-ban/5/dieu-chinh
        [HttpPost("/quan-ly/dat-ban/{id:long}/dieu-chinh")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            [FromRoute] long id,
            [FromForm] EditBookingViewModel input)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            input.Id = id;
            input.TableIds ??= new List<long>();
            ModelState.Remove(nameof(input.EndsAtLocal));

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        cancellationToken);

                await _context.Database.ExecuteSqlRawAsync(
                    """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'DoraFood.Reservations',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @LockResult < 0
                THROW 56027,
                    N'Không thể khóa dữ liệu đặt bàn. Vui lòng thử lại.',
                    1;
            """,
                    cancellationToken);

                var reservation = await _context.Set<Reservation>()
                    .AsTracking()
                    .Include(r => r.Tables)
                    .FirstOrDefaultAsync(
                        r => r.Id == id,
                        cancellationToken);

                if (reservation == null)
                {
                    return NotFound("Không tìm thấy yêu cầu đặt bàn.");
                }

                if (reservation.Status != "PENDING"
                    && reservation.Status != "CONFIRMED")
                {
                    TempData["BookingManagementError"] =
                        "Chỉ được điều chỉnh yêu cầu đang chờ hoặc đã xác nhận.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails",
                        new { id });
                }

                // Nạp lại dữ liệu để hiển thị form khi có lỗi.
                async Task<IActionResult> ShowFormAsync()
                {
                    await LoadEditBookingDataAsync(input, reservation);
                    return View("Edit", input);
                }

                if (!ModelState.IsValid)
                {
                    return await ShowFormAsync();
                }

                byte[] submittedVersion;

                try
                {
                    submittedVersion =
                        Convert.FromBase64String(input.Version);
                }
                catch (FormatException)
                {
                    TempData["BookingManagementError"] =
                        "Phiên bản dữ liệu không hợp lệ. "
                        + "Anh kiểm tra lại thông tin trên trang mới.";

                    return RedirectToRoute(
                        "DoraFoodEditBooking",
                        new { id });
                }

                if (!submittedVersion.SequenceEqual(reservation.Version))
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu đã được cập nhật bởi thao tác khác. "
                        + "Anh kiểm tra dữ liệu mới trước khi điều chỉnh.";

                    return RedirectToRoute(
                        "DoraFoodEditBooking",
                        new { id });
                }

                var now = DateTime.UtcNow;

                // Không cho đổi giờ để khôi phục một yêu cầu đã hết hạn.
                if (reservation.StartsAtUtc <= now.AddMinutes(-30))
                {
                    reservation.Status = "CANCELLED";

                    AddBookingHistory(
                        reservation,
                        "BOOKING_CANCELLED",
                        "Hủy yêu cầu đã quá hạn chờ 30 phút "
                            + "khi quản lý mở thao tác điều chỉnh.");

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    TempData["BookingManagementError"] =
                        "Yêu cầu đã quá hạn giữ bàn và được hủy. "
                        + "Không thể điều chỉnh yêu cầu này.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails",
                        new { id });
                }

                // Giờ Việt Nam; chỉ sửa yêu cầu trong ngày hiện tại.
                var startsAtLocal = DateTime.SpecifyKind(
                    input.StartsAtLocal!.Value,
                    DateTimeKind.Unspecified);

                var localNow = DateTime.SpecifyKind(
                    now.AddHours(7),
                    DateTimeKind.Unspecified);

                if (startsAtLocal.Date != localNow.Date)
                {
                    ModelState.AddModelError(
                        nameof(input.StartsAtLocal),
                        "Chỉ được điều chỉnh giờ đến trong ngày hiện tại.");
                }

                if (startsAtLocal <= localNow)
                {
                    ModelState.AddModelError(
                        nameof(input.StartsAtLocal),
                        "Giờ đến mới phải ở tương lai.");
                }

                var arrivalTime = startsAtLocal.TimeOfDay;

                var isLunch = arrivalTime >= TimeSpan.FromHours(10)
                    && arrivalTime < TimeSpan.FromHours(14);

                var isDinner = arrivalTime >= TimeSpan.FromHours(16)
                    && arrivalTime < TimeSpan.FromHours(22);

                if (!isLunch && !isDinner)
                {
                    ModelState.AddModelError(
                        nameof(input.StartsAtLocal),
                        "Giờ đến phải từ 10:00 đến trước 14:00 "
                            + "hoặc từ 16:00 đến trước 22:00.");
                }

                if (!ModelState.IsValid)
                {
                    return await ShowFormAsync();
                }

                // Mốc 18:00 chỉ chặn đặt mới, không chặn điều chỉnh.
                var sessionStartLocal = startsAtLocal.Date.AddHours(
                    isLunch ? 10 : 16);

                var sessionEndLocal = startsAtLocal.Date.AddHours(
                    isLunch ? 14 : 22);

                input.EndsAtLocal = sessionEndLocal;

                var offset = TimeSpan.FromHours(7);

                var startsAtUtc = new DateTimeOffset(
                    startsAtLocal, offset).UtcDateTime;

                var sessionStartUtc = new DateTimeOffset(
                    sessionStartLocal, offset).UtcDateTime;

                var endsAtUtc = new DateTimeOffset(
                    sessionEndLocal, offset).UtcDateTime;

                var tableIds = input.TableIds
                    .Distinct()
                    .ToArray();

                if (tableIds.Any(tableId => tableId <= 0))
                {
                    ModelState.AddModelError(
                        nameof(input.TableIds),
                        "Danh sách bàn không hợp lệ.");

                    return await ShowFormAsync();
                }

                if (reservation.Status == "CONFIRMED"
                    && tableIds.Length == 0)
                {
                    ModelState.AddModelError(
                        nameof(input.TableIds),
                        "Yêu cầu đã xác nhận phải có ít nhất một bàn.");

                    return await ShowFormAsync();
                }

                var selectedTables = await _context.Set<DiningTable>()
                    .AsTracking()
                    .Where(t =>
                        tableIds.Contains(t.Id)
                        && t.IsActive
                        && t.Area.IsActive)
                    .ToListAsync(cancellationToken);

                if (selectedTables.Count != tableIds.Length)
                {
                    ModelState.AddModelError(
                        nameof(input.TableIds),
                        "Có bàn không tồn tại hoặc bàn/khu vực đã ngừng hoạt động.");

                    return await ShowFormAsync();
                }

                if (selectedTables.Count > 0
                    && selectedTables.Sum(t => (int)t.Capacity)
                        < input.PartySize!.Value)
                {
                    ModelState.AddModelError(
                        nameof(input.TableIds),
                        "Tổng số chỗ của các bàn không đủ cho số khách.");

                    return await ShowFormAsync();
                }

                if (tableIds.Length > 0)
                {
                    var hasConflict = await _context.Set<Reservation>()
                        .AsNoTracking()
                        .AnyAsync(
                            r => r.Id != reservation.Id
                                && (
                                    r.Status == "SEATED"
                                    || (
                                        (r.Status == "PENDING"
                                            || r.Status == "CONFIRMED")
                                        && r.StartsAtUtc >= sessionStartUtc
                                        && r.StartsAtUtc < endsAtUtc
                                    )
                                )
                                && r.Tables.Any(
                                    t => tableIds.Contains(t.Id)),
                            cancellationToken);

                    if (hasConflict)
                    {
                        ModelState.AddModelError(
                            nameof(input.TableIds),
                            "Có bàn đã được giữ trong cùng buổi "
                            + "hoặc đang có khách chưa hoàn tất. "
                            + "Anh chọn bàn khác hoặc kiểm tra lượt trước.");

                        return await ShowFormAsync();
                    }
                }

                // Xác định những phần thực sự thay đổi.
                var changes = new List<string>();

                if (reservation.StartsAtUtc != startsAtUtc)
                {
                    changes.Add("giờ đến và hạn giữ bàn");
                }

                if (reservation.EndsAtUtc != endsAtUtc)
                {
                    changes.Add("buổi phục vụ/mốc giữ bàn nội bộ");
                }

                if (reservation.PartySize != input.PartySize!.Value)
                {
                    changes.Add("số khách");
                }

                var previousTableIds = reservation.Tables
                    .Select(t => t.Id)
                    .ToHashSet();

                var tablesChanged = !previousTableIds.SetEquals(tableIds);

                if (tablesChanged)
                {
                    changes.Add("các bàn đã phân");
                }

                if (changes.Count == 0)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Anh chưa thay đổi giờ, số khách hoặc bàn.");

                    return await ShowFormAsync();
                }

                reservation.StartsAtUtc = startsAtUtc;
                reservation.EndsAtUtc = endsAtUtc;
                reservation.PartySize = input.PartySize.Value;

                if (tablesChanged)
                {
                    reservation.Tables.Clear();

                    foreach (var table in selectedTables)
                    {
                        reservation.Tables.Add(table);
                    }
                }

                // Giữ nguyên trạng thái PENDING hoặc CONFIRMED.
                AddBookingHistory(
                    reservation,
                    "BOOKING_UPDATED",
                    "Điều chỉnh: "
                        + string.Join(", ", changes)
                        + ". Lý do: "
                        + input.Reason.Trim());

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã điều chỉnh yêu cầu đặt bàn {reservation.Code}.";

                return RedirectToRoute(
                    "DoraFoodManageBookingDetails",
                    new { id });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Dữ liệu đặt bàn {ReservationId} thay đổi khi điều chỉnh.",
                    id);

                TempData["BookingManagementError"] =
                    "Dữ liệu vừa được cập nhật bởi thao tác khác. "
                    + "Anh kiểm tra lại rồi thử lại.";
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Lỗi SQL khi điều chỉnh đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Chưa thể điều chỉnh lúc này. Anh vui lòng thử lại.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu điều chỉnh đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Không thể lưu điều chỉnh. Anh vui lòng thử lại.";
            }

            return RedirectToRoute(
                "DoraFoodEditBooking",
                new { id });
        }
        public class CancelBookingInput
        {
            [Required(ErrorMessage = "Thiếu phiên bản dữ liệu.")]
            [StringLength(64)]
            public string Version { get; set; } = string.Empty;

            [Required(ErrorMessage = "Vui lòng nhập lý do hủy.")]
            [StringLength(
                300,
                ErrorMessage = "Lý do hủy tối đa 300 ký tự.")]
            public string Reason { get; set; } = string.Empty;
        }
        // POST: /quan-ly/dat-ban/5/huy
        [HttpPost(
            "/quan-ly/dat-ban/{id:long}/huy",
            Name = "DoraFoodManageCancelBooking")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(
            [FromRoute] long id,
            [FromForm] CancelBookingInput input)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            // Các trường hợp lỗi đều quay lại trang chi tiết.
            IActionResult BackToDetails()
            {
                return RedirectToRoute(
                    "DoraFoodManageBookingDetails",
                    new { id });
            }

            if (!ModelState.IsValid)
            {
                TempData["BookingManagementError"] =
                    "Vui lòng nhập lý do hủy từ 1 đến 300 ký tự. "
                    + "Nếu dữ liệu đã cũ, anh tải lại trang rồi thử lại.";

                return BackToDetails();
            }

            byte[] submittedVersion;

            try
            {
                submittedVersion = Convert.FromBase64String(input.Version);
            }
            catch (FormatException)
            {
                TempData["BookingManagementError"] =
                    "Phiên bản dữ liệu không hợp lệ. Anh tải lại trang rồi thử lại.";

                return BackToDetails();
            }

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        cancellationToken);

                await _context.Database.ExecuteSqlRawAsync(
                    """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'DoraFood.Reservations',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @LockResult < 0
                THROW 56025,
                    N'Không thể khóa dữ liệu đặt bàn. Vui lòng thử lại.',
                    1;
            """,
                    cancellationToken);

                var reservation = await _context.Set<Reservation>()
                    .AsTracking()
                    .FirstOrDefaultAsync(
                        r => r.Id == id,
                        cancellationToken);

                if (reservation == null)
                {
                    return NotFound("Không tìm thấy yêu cầu đặt bàn.");
                }

                if (reservation.Status != "PENDING"
                    && reservation.Status != "CONFIRMED")
                {
                    TempData["BookingManagementError"] =
                        "Chỉ được hủy yêu cầu đang chờ hoặc đã xác nhận. "
                        + "Không thể hủy khi khách đã vào bàn.";

                    return BackToDetails();
                }

                if (!submittedVersion.SequenceEqual(reservation.Version))
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu đã được cập nhật. Anh kiểm tra lại trước khi hủy.";

                    return BackToDetails();
                }

                var previousStatus = reservation.Status;

                reservation.Status = "CANCELLED";

                // Giữ nguyên các bàn đã phân để lưu lịch sử.
                AddBookingHistory(
                    reservation,
                    "BOOKING_CANCELLED",
                    "Quản lý hủy đặt bàn từ trạng thái "
                        + previousStatus
                        + ". Lý do: "
                        + input.Reason.Trim());

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã hủy yêu cầu đặt bàn {reservation.Code}.";

                return BackToDetails();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Dữ liệu đặt bàn {ReservationId} thay đổi khi hủy.",
                    id);

                TempData["BookingManagementError"] =
                    "Dữ liệu vừa được cập nhật bởi thao tác khác. "
                    + "Anh kiểm tra lại rồi thử lại.";
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Lỗi SQL khi quản lý hủy đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Chưa thể hủy đặt bàn lúc này. Anh vui lòng thử lại.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu thao tác hủy đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Không thể lưu thao tác hủy. Anh vui lòng thử lại.";
            }

            return BackToDetails();
        }
        public class BookingDetailsViewModel
        {
            public Reservation Reservation { get; set; } = null!;

            public List<AuditLog> History { get; set; } = new();
        }

        // POST: /quan-ly/dat-ban/5/khong-den
        [HttpPost(
            "/quan-ly/dat-ban/{id:long}/khong-den",
            Name = "DoraFoodNoShowBooking")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NoShow(
            [FromRoute] long id,
            [FromForm] string? version)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            IActionResult BackToDetails()
            {
                return RedirectToRoute(
                    "DoraFoodManageBookingDetails",
                    new { id });
            }

            if (string.IsNullOrWhiteSpace(version)
                || version.Length > 64)
            {
                TempData["BookingManagementError"] =
                    "Thiếu phiên bản dữ liệu. Anh tải lại trang rồi thử lại.";

                return BackToDetails();
            }

            byte[] submittedVersion;

            try
            {
                submittedVersion = Convert.FromBase64String(version);
            }
            catch (FormatException)
            {
                TempData["BookingManagementError"] =
                    "Phiên bản dữ liệu không hợp lệ. Anh tải lại trang rồi thử lại.";

                return BackToDetails();
            }

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        cancellationToken);

                await _context.Database.ExecuteSqlRawAsync(
                    """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'DoraFood.Reservations',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @LockResult < 0
                THROW 56026,
                    N'Không thể khóa dữ liệu đặt bàn. Vui lòng thử lại.',
                    1;
            """,
                    cancellationToken);

                var reservation = await _context.Set<Reservation>()
                    .AsTracking()
                    .FirstOrDefaultAsync(
                        r => r.Id == id,
                        cancellationToken);

                if (reservation == null)
                {
                    return NotFound("Không tìm thấy yêu cầu đặt bàn.");
                }

                if (reservation.Status != "CONFIRMED")
                {
                    TempData["BookingManagementError"] =
                        "Chỉ được đánh dấu khách không đến "
                        + "đối với yêu cầu đã xác nhận.";

                    return BackToDetails();
                }

                if (!submittedVersion.SequenceEqual(reservation.Version))
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu đã được cập nhật. Anh kiểm tra lại rồi thử lại.";

                    return BackToDetails();
                }

                // Khách được chờ thêm 30 phút sau giờ bắt đầu.
                const int waitingMinutes = 30;

                var latestEligibleStart = DateTime.UtcNow
                    .AddMinutes(-waitingMinutes);

                if (reservation.StartsAtUtc > latestEligibleStart)
                {
                    TempData["BookingManagementError"] =
                        "Chưa đủ 30 phút kể từ giờ bắt đầu. "
                        + "Chưa thể đánh dấu khách không đến.";

                    return BackToDetails();
                }

                reservation.Status = "NO_SHOW";

                // Giữ liên kết bàn để lưu lịch sử.
                AddBookingHistory(
                    reservation,
                    "BOOKING_NO_SHOW",
                    "Quản lý xác nhận khách không đến "
                        + "sau thời gian chờ 30 phút kể từ giờ bắt đầu.");

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã đánh dấu khách không đến cho yêu cầu {reservation.Code}.";

                return BackToDetails();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Dữ liệu đặt bàn {ReservationId} thay đổi khi đánh dấu không đến.",
                    id);

                TempData["BookingManagementError"] =
                    "Dữ liệu vừa được cập nhật bởi thao tác khác. "
                    + "Anh kiểm tra lại rồi thử lại.";
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Lỗi SQL khi đánh dấu khách không đến cho đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Chưa thể cập nhật lúc này. Anh vui lòng thử lại.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu trạng thái khách không đến cho đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Không thể lưu trạng thái khách không đến. Anh vui lòng thử lại.";
            }

            return BackToDetails();
        }
        // GET: /quan-ly/dat-ban/5
        [HttpGet(
            "/quan-ly/dat-ban/{id:long}",
            Name = "DoraFoodManageBookingDetails")]
        public async Task<IActionResult> Details([FromRoute] long id)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            var cancellationToken = HttpContext.RequestAborted;

            // Lấy thông tin đặt bàn và các bàn đã phân.
            var reservation = await _context.Set<Reservation>()
                .AsNoTracking()
                .Include(r => r.Tables)
                    .ThenInclude(t => t.Area)
                .FirstOrDefaultAsync(
                    r => r.Id == id,
                    cancellationToken);

            if (reservation == null)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            // Dùng cùng định dạng khóa khi đọc và ghi lịch sử.
            var entityKey = id.ToString(
                System.Globalization.CultureInfo.InvariantCulture);

            // Lấy lịch sử và tài khoản thực hiện.
            var history = await _context.Set<AuditLog>()
                .AsNoTracking()
                .Include(log => log.Actor)
                .Where(log =>
                    log.EntityName == "Reservation"
                    && log.EntityKey == entityKey)
                .OrderByDescending(log => log.CreatedAtUtc)
                .ThenByDescending(log => log.Id)
                .ToListAsync(cancellationToken);

            var model = new BookingDetailsViewModel
            {
                Reservation = reservation,
                History = history
            };

            ViewData["Title"] =
                $"Chi tiết đặt bàn {reservation.Code}";

            return View(model);
        }
        public class BookingManagementViewModel
        {
            public List<Reservation> Reservations { get; set; } = new();

            public string Status { get; set; } = "PENDING";

            public string? Keyword { get; set; }

            public DateTime? ServiceDate { get; set; }

            public long? AreaId { get; set; }

            public List<DiningArea> Areas { get; set; } = new();

            public int CurrentPage { get; set; }

            public int TotalPages { get; set; }

            public int TotalItems { get; set; }
        }
        public class ConfirmBookingViewModel
        {
            // ID lấy từ URL, không nhận thay đổi từ form.
            [BindNever]
            public long Id { get; set; }

            [Required(ErrorMessage =
                "Thiếu phiên bản dữ liệu. Vui lòng tải lại trang.")]
            [StringLength(64)]
            public string Version { get; set; } = string.Empty;

            [MinLength(1,
                ErrorMessage = "Vui lòng chọn ít nhất một bàn.")]
            [MaxLength(200,
                ErrorMessage = "Số lượng bàn được chọn vượt giới hạn.")]
            public List<long> TableIds { get; set; } = new();

            // Thông tin hiển thị do server đọc từ database.
            [BindNever]
            [ValidateNever]
            public Reservation Reservation { get; set; } = null!;

            [BindNever]
            [ValidateNever]
            public List<BookingTableOption> Tables { get; set; } = new();
        }

        public class BookingTableOption
        {
            public long Id { get; set; }

            public string Code { get; set; } = string.Empty;

            public string AreaName { get; set; } = string.Empty;

            public short Capacity { get; set; }

            public bool IsAvailable { get; set; }
        }
        // Kiểm tra lại tài khoản và quyền hiện tại trong database.
        public override async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            if (!long.TryParse(
                    User.FindFirstValue(
                        ClaimTypes.NameIdentifier),
                    out var userId)
                || !Guid.TryParse(
                    User.FindFirstValue("SecurityStamp"),
                    out var securityStamp))
            {
                await RedirectToLoginAsync(context);
                return;
            }

            var account = await _context
                .Set<DoraFood.Models.User>()
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.IsActive,
                    u.SecurityStamp,

                    CanManage = u.Roles.Any(r =>
                        r.Code == "ADMIN"
                        || r.Code == "MANAGER")
                })
                .FirstOrDefaultAsync(
                    HttpContext.RequestAborted);

            if (account == null
                || !account.IsActive
                || account.SecurityStamp != securityStamp)
            {
                await RedirectToLoginAsync(context);
                return;
            }

            if (!account.CanManage)
            {
                context.Result = Forbid();
                return;
            }

            await next();
        }
        public class TableMapViewModel
        {
            public DateTime ServiceDate { get; set; }

            public string Session { get; set; } = "LUNCH";

            public string SessionLabel { get; set; } = string.Empty;

            // Mốc của buổi để hiển thị; không phải giờ kết thúc dùng bữa.
            public DateTime StartsAtLocal { get; set; }

            public DateTime EndsAtLocal { get; set; }

            public long? AreaId { get; set; }

            public List<DiningArea> Areas { get; set; } = new();

            public List<TableMapItem> Tables { get; set; } = new();
        }

        // GET: /quan-ly/so-do-ban
        [HttpGet(
            "/quan-ly/so-do-ban",
            Name = "DoraFoodTableMap")]
        public async Task<IActionResult> TableMap(
            DateTime? serviceDate = null,
            string? session = null,
            long? areaId = null)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Ngày, buổi phục vụ hoặc khu vực không hợp lệ.");
            }

            var localNow = DateTime.UtcNow.AddHours(7);
            var selectedDate = DateTime.SpecifyKind(
                serviceDate?.Date ?? localNow.Date,
                DateTimeKind.Unspecified);

            session = string.IsNullOrWhiteSpace(session)
                ? (localNow.TimeOfDay < TimeSpan.FromHours(14) ? "LUNCH" : "DINNER")
                : session.Trim().ToUpperInvariant();

            if (session != "LUNCH" && session != "DINNER")
            {
                return BadRequest("Buổi phục vụ không hợp lệ.");
            }

            if (areaId.HasValue && areaId.Value <= 0)
            {
                return BadRequest("Khu vực không hợp lệ.");
            }

            var isLunch = session == "LUNCH";
            var localStart = selectedDate.AddHours(isLunch ? 10 : 16);
            var localEnd = selectedDate.AddHours(isLunch ? 14 : 22);
            var offset = TimeSpan.FromHours(7);
            var sessionStartUtc = new DateTimeOffset(localStart, offset).UtcDateTime;
            var sessionEndUtc = new DateTimeOffset(localEnd, offset).UtcDateTime;
            var cancellationToken = HttpContext.RequestAborted;

            var areas = await _context.Set<DiningArea>()
                .AsNoTracking()
                .OrderBy(a => a.Name)
                .ThenBy(a => a.Id)
                .ToListAsync(cancellationToken);

            if (areaId.HasValue && !areas.Any(a => a.Id == areaId.Value))
            {
                return BadRequest("Khu vực không tồn tại.");
            }

            var query = _context.Set<DiningTable>().AsNoTracking();

            if (areaId.HasValue)
            {
                var selectedAreaId = areaId.Value;
                query = query.Where(t => t.AreaId == selectedAreaId);
            }

            var tables = await query
                .Include(t => t.Area)
                .Include(t => t.Reservations.Where(r =>
                    r.Status == "SEATED"
                    || (
                        (r.Status == "PENDING" || r.Status == "CONFIRMED")
                        && r.StartsAtUtc >= sessionStartUtc
                        && r.StartsAtUtc < sessionEndUtc
                    )))
                .AsSplitQuery()
                .OrderBy(t => t.Area.Name)
                .ThenBy(t => t.AreaId)
                .ThenBy(t => t.Code)
                .ThenBy(t => t.Id)
                .ToListAsync(cancellationToken);

            var model = new TableMapViewModel
            {
                ServiceDate = selectedDate,
                Session = session,
                SessionLabel = isLunch ? "Buổi trưa" : "Buổi tối",
                StartsAtLocal = localStart,
                EndsAtLocal = localEnd,
                AreaId = areaId,
                Areas = areas,
                Tables = tables.Select(t => new TableMapItem
                {
                    Id = t.Id,
                    AreaId = t.AreaId,
                    Code = t.Code,
                    AreaName = t.Area.Name,
                    Capacity = t.Capacity,
                    IsActive = t.IsActive && t.Area.IsActive,
                    IsOccupiedNow = t.Reservations.Any(r => r.Status == "SEATED"),
                    HasScheduleConflict = t.Reservations.Any(r =>
                        (r.Status == "PENDING" || r.Status == "CONFIRMED" || r.Status == "SEATED")
                        && r.StartsAtUtc >= sessionStartUtc
                        && r.StartsAtUtc < sessionEndUtc),
                    Reservations = t.Reservations
                        .OrderBy(r => r.StartsAtUtc)
                        .ThenBy(r => r.Id)
                        .ToList()
                }).ToList()
            };

            ViewData["Title"] = "Sơ đồ bàn theo buổi";
            return View(model);
        }
        public class TableMapItem
        {
            public long Id { get; set; }

            public long AreaId { get; set; }

            public string Code { get; set; } = string.Empty;

            public string AreaName { get; set; } = string.Empty;

            public short Capacity { get; set; }

            // Bàn và khu vực đều đang hoạt động.
            public bool IsActive { get; set; }

            // Có khách đang ngồi, độc lập với khoảng thời gian lọc.
            public bool IsOccupiedNow { get; set; }

            // Có yêu cầu còn hiệu lực trong ngày và buổi được chọn.
            public bool HasScheduleConflict { get; set; }

            // Các yêu cầu liên quan để hiển thị trên giao diện.
            public List<Reservation> Reservations { get; set; } = new();
        }
        // GET: /quan-ly/dat-ban
        [HttpGet(
            "/quan-ly/dat-ban",
            Name = "DoraFoodManageBookings")]
        [HttpGet("/BookingManagement")]
        public async Task<IActionResult> Index(
            string? status = "PENDING",
            string? keyword = null,
            DateTime? serviceDate = null,
            long? areaId = null,
            int page = 1)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(
                    "Tham số lọc hoặc số trang không hợp lệ.");
            }

            status = string.IsNullOrWhiteSpace(status)
                ? "PENDING"
                : status.Trim().ToUpperInvariant();

            if (!AllowedStatuses.Contains(status))
            {
                return BadRequest(
                    "Trạng thái đặt bàn không hợp lệ.");
            }

            keyword = string.IsNullOrWhiteSpace(keyword)
                ? null
                : keyword.Trim();

            if (keyword != null && keyword.Length > 150)
            {
                return BadRequest(
                    "Từ khóa tìm kiếm tối đa 150 ký tự.");
            }

            if (areaId.HasValue && areaId.Value <= 0)
            {
                return BadRequest("Khu vực không hợp lệ.");
            }

            const int pageSize = 10;

            var cancellationToken = HttpContext.RequestAborted;

            // Bao gồm cả khu vực ngừng hoạt động
            // để vẫn tìm được các lượt đặt bàn trong lịch sử.
            var areas = await _context.Set<DiningArea>()
                .AsNoTracking()
                .OrderBy(a => a.Name)
                .ThenBy(a => a.Id)
                .ToListAsync(cancellationToken);

            if (areaId.HasValue
                && !areas.Any(a => a.Id == areaId.Value))
            {
                return BadRequest(
                    "Khu vực được chọn không tồn tại.");
            }

            var query = _context.Set<Reservation>()
                .AsNoTracking();

            // 1. Lọc trạng thái.
            if (status != "ALL")
            {
                query = query.Where(r => r.Status == status);
            }

            // 2. Tìm theo mã đặt bàn, tên hoặc số điện thoại.
            if (keyword != null)
            {
                query = query.Where(r =>
                    r.Code.Contains(keyword)
                    || r.GuestName.Contains(keyword)
                    || r.GuestPhone.Contains(keyword));
            }

            // 3. Lọc ngày bắt đầu phục vụ theo giờ Việt Nam.
            if (serviceDate.HasValue)
            {
                var localDate = DateTime.SpecifyKind(
                    serviceDate.Value.Date,
                    DateTimeKind.Unspecified);

                DateTime dayStartUtc;
                DateTime dayEndUtc;

                try
                {
                    var localDayStart = new DateTimeOffset(
                        localDate,
                        TimeSpan.FromHours(7));

                    dayStartUtc = localDayStart.UtcDateTime;

                    dayEndUtc = localDayStart
                        .AddDays(1)
                        .UtcDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return BadRequest(
                        "Ngày phục vụ nằm ngoài khoảng được hỗ trợ.");
                }

                query = query.Where(r =>
                    r.StartsAtUtc >= dayStartUtc
                    && r.StartsAtUtc < dayEndUtc);

                serviceDate = localDate;
            }

            // 4. Lọc theo khu vực của ít nhất một bàn đã phân.
            if (areaId.HasValue)
            {
                var selectedAreaId = areaId.Value;

                query = query.Where(r =>
                    r.Tables.Any(t => t.AreaId == selectedAreaId));
            }

            // Đếm sau khi áp dụng toàn bộ điều kiện.
            var totalItems = await query.CountAsync(
                cancellationToken);

            var totalPages = (int)Math.Ceiling(
                totalItems / (double)pageSize);

            page = Math.Clamp(
                page,
                1,
                Math.Max(1, totalPages));

            var reservations = await query
                .Include(r => r.Tables)
                    .ThenInclude(t => t.Area)
                .AsSplitQuery()
                .OrderByDescending(r => r.CreatedAtUtc)
                .ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            ViewData["Title"] = "Quản lý đặt bàn";

            return View(new BookingManagementViewModel
            {
                Reservations = reservations,
                Status = status,
                Keyword = keyword,
                ServiceDate = serviceDate,
                AreaId = areaId,
                Areas = areas,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalItems = totalItems
            });
        }
        // GET: /quan-ly/dat-ban/{id}/xac-nhan
        [HttpGet(
            "/quan-ly/dat-ban/{id:long}/xac-nhan",
            Name = "DoraFoodConfirmBooking")]
        public async Task<IActionResult> Confirm([FromRoute] long id)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            var reservation = await _context.Set<Reservation>()
                .AsNoTracking()
                .Include(r => r.Tables)
                .FirstOrDefaultAsync(
                    r => r.Id == id,
                    HttpContext.RequestAborted);

            if (reservation == null)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            if (reservation.Status != "PENDING")
            {
                TempData["BookingManagementError"] =
                    "Chỉ xác nhận yêu cầu đang chờ xử lý.";

                return RedirectToRoute("DoraFoodManageBookings");
            }

            var now = DateTime.UtcNow;

            if (reservation.StartsAtUtc <= now.AddMinutes(-30))
            {
                TempData["BookingManagementError"] =
                    "Yêu cầu đã quá hạn giữ bàn 30 phút, không thể xác nhận.";

                return RedirectToRoute(
                    "DoraFoodManageBookingDetails", new { id });
            }

            if (reservation.StartsAtUtc.AddHours(7).Date != now.AddHours(7).Date
                || !TryGetServiceSessionBounds(
                    reservation.StartsAtUtc, out _, out _))
            {
                TempData["BookingManagementError"] =
                    "Chỉ xác nhận yêu cầu trong ngày hiện tại "
                    + "và có giờ đến trong khung phục vụ.";

                return RedirectToRoute(
                    "DoraFoodManageBookingDetails", new { id });
            }

            var model = await BuildConfirmModelAsync(reservation);

            return View(model);
        }
        // POST: /quan-ly/dat-ban/{id}/xac-nhan
        [HttpPost("/quan-ly/dat-ban/{id:long}/xac-nhan")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm(
            [FromRoute] long id,
            ConfirmBookingViewModel input)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            input.Id = id;
            input.TableIds ??= new List<long>();

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        cancellationToken);

                // Cùng khóa với thao tác hủy và quy trình đặt bàn.
                await _context.Database.ExecuteSqlRawAsync(
                    @"
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'DoraFood.Reservations',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @LockResult < 0
                THROW 56022,
                    N'Lịch đặt bàn đang bận. Vui lòng thử lại.',
                    1;
            ",
                    cancellationToken);

                // Đọc lại dữ liệu sau khi đã lấy khóa.
                var reservation = await _context.Set<Reservation>()
                    .AsTracking()
                    .Include(r => r.Tables)
                    .FirstOrDefaultAsync(
                        r => r.Id == id,
                        cancellationToken);

                if (reservation == null)
                {
                    return NotFound("Không tìm thấy yêu cầu đặt bàn.");
                }

                if (reservation.Status != "PENDING")
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu không còn ở trạng thái chờ xác nhận.";

                    return RedirectToRoute("DoraFoodManageBookings");
                }

                if (!ModelState.IsValid)
                {
                    return View(
                        "Confirm",
                        await BuildConfirmModelAsync(reservation, input));
                }

                // Kiểm tra phiên bản dữ liệu mà người quản lý đã mở.
                byte[] submittedVersion;

                try
                {
                    submittedVersion = Convert.FromBase64String(
                        input.Version);
                }
                catch (FormatException)
                {
                    TempData["BookingManagementError"] =
                        "Phiên bản dữ liệu không hợp lệ. "
                        + "Vui lòng kiểm tra và chọn bàn lại.";

                    return RedirectToAction(
                        nameof(Confirm),
                        new { id });
                }

                if (!submittedVersion.SequenceEqual(reservation.Version))
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu đã được cập nhật sau khi bạn mở trang. "
                        + "Vui lòng kiểm tra và chọn bàn lại.";

                    return RedirectToAction(
                        nameof(Confirm),
                        new { id });
                }

                var now = DateTime.UtcNow;

                // Kiểm tra lại hạn sau khi lấy khóa và đọc phiên bản dữ liệu.
                if (reservation.StartsAtUtc <= now.AddMinutes(-30))
                {
                    reservation.Status = "CANCELLED";

                    AddBookingHistory(
                        reservation,
                        "BOOKING_CANCELLED",
                        "Hủy do quá hạn chờ 30 phút "
                            + "khi quản lý thực hiện xác nhận.");

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    TempData["BookingManagementError"] =
                        "Yêu cầu đã quá hạn giữ bàn và được hủy.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails", new { id });
                }

                if (reservation.StartsAtUtc.AddHours(7).Date != now.AddHours(7).Date
                    || !TryGetServiceSessionBounds(
                        reservation.StartsAtUtc,
                        out var sessionStartUtc,
                        out var sessionEndUtc))
                {
                    TempData["BookingManagementError"] =
                        "Giờ đến phải trong ngày hiện tại "
                        + "và trong khung phục vụ của nhà hàng.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails", new { id });
                }

                var tableIds = input.TableIds
                    .Distinct()
                    .ToArray();

                if (tableIds.Length == 0 || tableIds.Any(tableId => tableId <= 0))
                {
                    ModelState.AddModelError(
                        nameof(ConfirmBookingViewModel.TableIds),
                        "Vui lòng chọn bàn hợp lệ.");

                    return View(
                        "Confirm",
                        await BuildConfirmModelAsync(reservation, input));
                }

                // Đọc lại những bàn đang hoạt động.
                var selectedTables = await _context.Set<DiningTable>()
                    .AsTracking()
                    .Where(t =>
                        tableIds.Contains(t.Id)
                        && t.IsActive
                        && t.Area.IsActive)
                    .ToListAsync(cancellationToken);

                if (selectedTables.Count != tableIds.Length)
                {
                    ModelState.AddModelError(
                        nameof(ConfirmBookingViewModel.TableIds),
                        "Có bàn không tồn tại hoặc đã ngừng phục vụ.");

                    return View(
                        "Confirm",
                        await BuildConfirmModelAsync(reservation, input));
                }

                // Cộng sức chứa bằng int để tránh giới hạn của short.
                var totalCapacity = selectedTables.Sum(
                    t => (int)t.Capacity);

                if (totalCapacity < reservation.PartySize)
                {
                    ModelState.AddModelError(
                        nameof(ConfirmBookingViewModel.TableIds),
                        $"Các bàn đã chọn có {totalCapacity} chỗ, "
                        + $"chưa đủ cho {reservation.PartySize} khách.");

                    return View(
                        "Confirm",
                        await BuildConfirmModelAsync(reservation, input));
                }

                // Kiểm tra lại lịch của toàn bộ các bàn đã chọn.
                var hasConflict = await _context.Set<Reservation>()
                    .AsNoTracking()
                    .AnyAsync(
                        r => r.Id != reservation.Id
                            && (
                                r.Status == "SEATED"
                                || (
                                    (r.Status == "PENDING"
                                        || r.Status == "CONFIRMED")
                                    && r.StartsAtUtc >= sessionStartUtc
                                    && r.StartsAtUtc < sessionEndUtc
                                )
                            )
                            && r.Tables.Any(t => tableIds.Contains(t.Id)),
                        cancellationToken);

                if (hasConflict)
                {
                    ModelState.AddModelError(
                        nameof(ConfirmBookingViewModel.TableIds),
                        "Một hoặc nhiều bàn đã có lịch trùng. "
                        + "Vui lòng chọn bàn khác.");

                    return View(
                        "Confirm",
                        await BuildConfirmModelAsync(reservation, input));
                }

                // Gán tập bàn đã chọn cho yêu cầu hiện tại.
                reservation.Tables.Clear();

                foreach (var table in selectedTables)
                {
                    reservation.Tables.Add(table);
                }

                // Mốc nội bộ là cuối buổi; không phải giờ khách phải rời bàn.
                reservation.EndsAtUtc = sessionEndUtc;
                reservation.Status = "CONFIRMED";

                // Ghi lại thao tác xác nhận và phân bàn.
                AddBookingHistory(
                    reservation,
                    "BOOKING_CONFIRMED",
                    "Xác nhận đặt bàn và phân "
                        + selectedTables.Count
                        + " bàn cho "
                        + reservation.PartySize
                        + " khách.");

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã xác nhận và phân bàn cho yêu cầu {reservation.Code}.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "CONFIRMED" });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Yêu cầu đặt bàn {ReservationId} bị cập nhật đồng thời.",
                    id);

                TempData["BookingManagementError"] =
                    "Yêu cầu vừa được người khác cập nhật. "
                    + "Vui lòng kiểm tra lại.";
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Lỗi SQL khi xác nhận đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Chưa thể xác nhận lúc này. Vui lòng thử lại.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu xác nhận đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Chưa thể lưu kết quả xác nhận. Vui lòng thử lại.";
            }

            return RedirectToAction(
                nameof(Confirm),
                new { id });
        }
        // POST: /quan-ly/dat-ban/5/nhan-khach
        [HttpPost(
            "/quan-ly/dat-ban/{id:long}/nhan-khach",
            Name = "DoraFoodSeatBooking")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Seat(
            [FromRoute] long id,
            [FromForm] string? version)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            // Phiên bản dữ liệu được gửi từ giao diện.
            byte[] submittedVersion;

            if (string.IsNullOrWhiteSpace(version)
                || version.Length > 64)
            {
                TempData["BookingManagementError"] =
                    "Thiếu phiên bản dữ liệu. Anh tải lại trang rồi thử lại.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "CONFIRMED" });
            }

            try
            {
                submittedVersion = Convert.FromBase64String(version);
            }
            catch (FormatException)
            {
                TempData["BookingManagementError"] =
                    "Phiên bản dữ liệu không hợp lệ. Anh tải lại trang rồi thử lại.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "CONFIRMED" });
            }

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        cancellationToken);

                // Dùng cùng khóa với chức năng xác nhận và phân bàn.
                await _context.Database.ExecuteSqlRawAsync(
                    """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'DoraFood.Reservations',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @LockResult < 0
                THROW 56023,
                    N'Không thể khóa dữ liệu đặt bàn. Vui lòng thử lại.',
                    1;
            """,
                    cancellationToken);

                var reservation = await _context.Set<Reservation>()
                    .AsTracking()
                    .Include(r => r.Tables)
                        .ThenInclude(t => t.Area)
                    .FirstOrDefaultAsync(
                        r => r.Id == id,
                        cancellationToken);

                if (reservation == null)
                {
                    return NotFound("Không tìm thấy yêu cầu đặt bàn.");
                }

                // Chỉ nhận khách đối với yêu cầu đã xác nhận.
                if (reservation.Status != "CONFIRMED")
                {
                    TempData["BookingManagementError"] =
                        "Chỉ có thể nhận khách đối với yêu cầu đã xác nhận.";

                    return RedirectToRoute(
                        "DoraFoodManageBookings",
                        new { status = "CONFIRMED" });
                }

                // Chặn thao tác từ một trang đã hiển thị dữ liệu cũ.
                if (!submittedVersion.SequenceEqual(reservation.Version))
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu đã được cập nhật. Anh tải lại trang rồi kiểm tra lại.";

                    return RedirectToRoute(
                        "DoraFoodManageBookings",
                        new { status = "CONFIRMED" });
                }

                // Hạn khách đến = giờ hẹn + 30 phút.
                // Không dùng giờ đóng cửa làm hạn nhận khách.
                var arrivalDeadlineUtc =
                    reservation.StartsAtUtc.AddMinutes(30);

                if (DateTime.UtcNow >= arrivalDeadlineUtc)
                {
                    reservation.Status = "CANCELLED";

                    AddBookingHistory(
                        reservation,
                        "BOOKING_CANCELLED",
                        "Hủy do khách chưa được nhận vào bàn "
                            + "sau 30 phút kể từ giờ hẹn.");

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    TempData["BookingManagementError"] =
                        "Đã quá hạn giữ bàn 30 phút. "
                        + "Yêu cầu đã được hủy, không thể nhận khách.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails",
                        new { id });
                }

                // Kiểm tra các bàn đã phân.
                if (reservation.Tables.Count == 0
                    || reservation.Tables.Any(
                        t => !t.IsActive || !t.Area.IsActive))
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu chưa có bàn hoặc có bàn/khu vực đã ngừng hoạt động.";

                    return RedirectToRoute(
                        "DoraFoodManageBookings",
                        new { status = "CONFIRMED" });
                }

                var totalCapacity = reservation.Tables.Sum(
                    t => (int)t.Capacity);

                if (totalCapacity < reservation.PartySize)
                {
                    TempData["BookingManagementError"] =
                        "Các bàn đã phân không còn đủ chỗ cho số khách.";

                    return RedirectToRoute(
                        "DoraFoodManageBookings",
                        new { status = "CONFIRMED" });
                }

                var tableIds = reservation.Tables
                    .Select(t => t.Id)
                    .ToArray();

                // Bàn còn SEATED vẫn đang có khách cho đến khi hoàn tất lượt phục vụ.
                var hasSeatedGuests = await _context.Set<Reservation>()
                    .AsNoTracking()
                    .AnyAsync(
                        r => r.Id != reservation.Id
                            && r.Status == "SEATED"
                            && r.Tables.Any(t => tableIds.Contains(t.Id)),
                        cancellationToken);

                if (hasSeatedGuests)
                {
                    TempData["BookingManagementError"] =
                        "Có bàn đang phục vụ khách khác. "
                        + "Cần kiểm tra và hoàn tất lượt khách trước khi nhận khách mới.";

                    return RedirectToRoute(
                        "DoraFoodManageBookings",
                        new { status = "CONFIRMED" });
                }

                // Giữ nguyên các bàn đã phân và thời gian đã đặt.
                reservation.Status = "SEATED";

                // Ghi lịch sử nhận khách.
                AddBookingHistory(
                    reservation,
                    "BOOKING_SEATED",
                    "Nhận khách vào các bàn đã phân cho "
                        + reservation.PartySize
                        + " khách.");

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã nhận khách cho yêu cầu {reservation.Code}.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "SEATED" });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Dữ liệu đặt bàn {ReservationId} thay đổi khi nhận khách.",
                    id);

                TempData["BookingManagementError"] =
                    "Dữ liệu vừa được cập nhật bởi thao tác khác. "
                    + "Anh tải lại trang rồi thử lại.";
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Lỗi SQL khi nhận khách cho đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Chưa thể nhận khách lúc này. Anh vui lòng thử lại.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu trạng thái nhận khách cho đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Không thể lưu trạng thái nhận khách. Anh vui lòng thử lại.";
            }

            return RedirectToRoute(
                "DoraFoodManageBookings",
                new { status = "CONFIRMED" });
        }
        // POST: /quan-ly/dat-ban/5/hoan-tat
        [HttpPost(
            "/quan-ly/dat-ban/{id:long}/hoan-tat",
            Name = "DoraFoodCompleteBooking")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(
            [FromRoute] long id,
            [FromForm] string? version)
        {
            if (id <= 0)
            {
                return NotFound("Không tìm thấy yêu cầu đặt bàn.");
            }

            byte[] submittedVersion;

            if (string.IsNullOrWhiteSpace(version)
                || version.Length > 64)
            {
                TempData["BookingManagementError"] =
                    "Thiếu phiên bản dữ liệu. Anh tải lại trang rồi thử lại.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "SEATED" });
            }

            try
            {
                submittedVersion = Convert.FromBase64String(version);
            }
            catch (FormatException)
            {
                TempData["BookingManagementError"] =
                    "Phiên bản dữ liệu không hợp lệ. Anh tải lại trang rồi thử lại.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "SEATED" });
            }

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        cancellationToken);

                // Dùng cùng khóa với xác nhận bàn và nhận khách.
                await _context.Database.ExecuteSqlRawAsync(
                    """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'DoraFood.Reservations',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;

            IF @LockResult < 0
                THROW 56024,
                    N'Không thể khóa dữ liệu đặt bàn. Vui lòng thử lại.',
                    1;
            """,
                    cancellationToken);

                var reservation = await _context.Set<Reservation>()
                    .AsTracking()
                    .FirstOrDefaultAsync(
                        r => r.Id == id,
                        cancellationToken);

                if (reservation == null)
                {
                    return NotFound("Không tìm thấy yêu cầu đặt bàn.");
                }

                // Chỉ hoàn tất khi khách đang sử dụng bàn.
                if (reservation.Status != "SEATED")
                {
                    TempData["BookingManagementError"] =
                        "Chỉ có thể hoàn tất yêu cầu đang ở trạng thái khách đã vào bàn.";

                    return RedirectToRoute(
                        "DoraFoodManageBookings",
                        new { status = "SEATED" });
                }

                // Kiểm tra dữ liệu có thay đổi từ lúc mở trang hay không.
                if (!submittedVersion.SequenceEqual(reservation.Version))
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu đã được cập nhật. Anh tải lại trang rồi kiểm tra lại.";

                    return RedirectToRoute(
                        "DoraFoodManageBookings",
                        new { status = "SEATED" });
                }

                // Giữ nguyên liên kết bàn để lưu lịch sử.
                reservation.Status = "COMPLETED";

                // Ghi lịch sử hoàn tất lượt đặt bàn.
                AddBookingHistory(
                    reservation,
                    "BOOKING_COMPLETED",
                    "Hoàn tất lượt đặt bàn sau khi khách rời bàn.");

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã hoàn tất lượt đặt bàn {reservation.Code}.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "COMPLETED" });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Dữ liệu đặt bàn {ReservationId} thay đổi khi hoàn tất.",
                    id);

                TempData["BookingManagementError"] =
                    "Dữ liệu vừa được cập nhật bởi thao tác khác. "
                    + "Anh tải lại trang rồi thử lại.";
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Lỗi SQL khi hoàn tất đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Chưa thể hoàn tất lượt đặt bàn lúc này. Anh vui lòng thử lại.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu trạng thái hoàn tất đặt bàn {ReservationId}.",
                    id);

                TempData["BookingManagementError"] =
                    "Không thể lưu trạng thái hoàn tất. Anh vui lòng thử lại.";
            }

            return RedirectToRoute(
                "DoraFoodManageBookings",
                new { status = "SEATED" });
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

                    // Không có lượt đang ngồi và không bị giữ trong cùng buổi.
                    IsAvailable = !t.Reservations.Any(r =>
                        r.Id != reservationId
                        && (
                            r.Status == "SEATED"
                            || (
                                (r.Status == "PENDING"
                                    || r.Status == "CONFIRMED")
                                && r.StartsAtUtc >= sessionStartUtc
                                && r.StartsAtUtc < sessionEndUtc
                            )
                        ))
                })
                .ToListAsync(HttpContext.RequestAborted);

            ViewData["Title"] = "Chọn bàn và xác nhận";

            return model;
        }
        // Xác định buổi theo giờ đến tại Việt Nam.
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
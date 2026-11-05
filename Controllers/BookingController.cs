using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Controllers
{
    public class BookingController : Controller
    {
        private readonly DoraFoodContext _context;
        private readonly ILogger<BookingController> _logger;

        private static readonly TimeZoneInfo RestaurantTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                "SE Asia Standard Time");

        public BookingController(
            DoraFoodContext context,
            ILogger<BookingController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Dữ liệu nhận từ form đặt bàn.
        public class BookingInput
        {
            [Display(Name = "Họ và tên")]
            [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
            [StringLength(
                150,
                ErrorMessage = "Họ và tên tối đa 150 ký tự.")]
            public string GuestName { get; set; } = string.Empty;

            [Display(Name = "Số điện thoại")]
            [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
            [Phone(
                ErrorMessage = "Số điện thoại không đúng định dạng.")]
            [StringLength(
                20,
                ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
            public string GuestPhone { get; set; } = string.Empty;

            [Display(Name = "Số khách")]
            [Required(ErrorMessage = "Vui lòng nhập số khách.")]
            [Range(
                1,
                short.MaxValue,
                ErrorMessage = "Số khách phải từ 1 đến 32.767.")]
            public short? PartySize { get; set; }

            [Display(Name = "Giờ đến dự kiến")]
            [Required(ErrorMessage = "Vui lòng chọn giờ đến.")]
            [DataType(DataType.DateTime)]
            public DateTime? StartsAtLocal { get; set; }

            // Giữ thuộc tính để View cũ chưa bị lỗi.
            // Không nhận giá trị do khách gửi lên.
            // Hệ thống tự tính giờ kết thúc buổi phục vụ.
            [BindNever]
            [ValidateNever]
            [Display(Name = "Kết thúc buổi phục vụ")]
            public DateTime? EndsAtLocal { get; set; }

            [Display(Name = "Ghi chú")]
            [StringLength(
                1000,
                ErrorMessage = "Ghi chú tối đa 1.000 ký tự.")]
            public string? Note { get; set; }
        }

        public class MyBookingsViewModel
        {
            public List<Reservation> Reservations { get; set; } = new();

            public int CurrentPage { get; set; }

            public int TotalPages { get; set; }

            public int TotalItems { get; set; }
        }

        // GET: /dat-ban
        [HttpGet("/dat-ban", Name = "DoraFoodBooking")]
        [HttpGet("/book.html")]
        [HttpGet("/Booking")]
        [HttpGet("/Booking/Index")]
        [AllowAnonymous]
        public IActionResult Index()
        {
            var localNow = GetRestaurantTime(DateTime.UtcNow);

            // Gợi ý giờ đến sau hiện tại 15 phút.
            var suggested = localNow.AddMinutes(15);

            suggested = new DateTime(
                suggested.Year,
                suggested.Month,
                suggested.Day,
                suggested.Hour,
                suggested.Minute,
                0,
                DateTimeKind.Unspecified);

            var time = suggested.TimeOfDay;

            if (time < TimeSpan.FromHours(10))
            {
                suggested = suggested.Date.AddHours(10);
            }
            else if (time >= TimeSpan.FromHours(14)
                && time < TimeSpan.FromHours(16))
            {
                suggested = suggested.Date.AddHours(16);
            }
            else if (time >= TimeSpan.FromHours(22))
            {
                suggested = suggested.Date.AddDays(1).AddHours(10);
            }

            var model = new BookingInput
            {
                PartySize = 2,
                StartsAtLocal = suggested,

                EndsAtLocal = suggested.Date.AddHours(
                    suggested.TimeOfDay < TimeSpan.FromHours(14)
                        ? 14
                        : 22)
            };

            return View(model);
        }

        // POST: /dat-ban
        [HttpPost("/dat-ban")]
        [HttpPost("/book.html")]
        [HttpPost("/Booking")]
        [HttpPost("/Booking/Index")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(BookingInput input)
        {
            if (!TryPrepareBooking(
                    input,
                    DateTime.UtcNow,
                    out var startsAtUtc,
                    out var sessionEndsAtUtc))
            {
                return View(input);
            }

            long? customerId = null;

            // Khách được gửi yêu cầu mà không cần đăng nhập.
            // Nếu đăng nhập thì liên kết với đúng tài khoản.
            if (User.Identity?.IsAuthenticated == true)
            {
                customerId = await GetActiveCustomerIdAsync();

                if (!customerId.HasValue)
                {
                    return await RestartBookingLoginAsync();
                }
            }

            // Kiểm tra lại sau khi đọc tài khoản,
            // tránh xử lý kéo dài qua 18:00 hoặc qua giờ hẹn.
            var acceptedAtUtc = DateTime.UtcNow;

            if (!TryPrepareBooking(
                    input,
                    acceptedAtUtc,
                    out startsAtUtc,
                    out sessionEndsAtUtc))
            {
                return View(input);
            }

            var reservation = new Reservation
            {
                Code = "DB-" + Guid.NewGuid().ToString("N"),

                CustomerId = customerId,

                GuestName = input.GuestName.Trim(),
                GuestPhone = input.GuestPhone.Trim(),
                PartySize = input.PartySize!.Value,

                StartsAtUtc = startsAtUtc,

                // Tạm dùng giờ kết thúc buổi phục vụ
                // để tương thích với cấu trúc SQL hiện tại.
                EndsAtUtc = sessionEndsAtUtc,

                Status = "PENDING",

                Note = string.IsNullOrWhiteSpace(input.Note)
                    ? null
                    : input.Note.Trim(),

                CreatedBy = null,
                CreatedAtUtc = acceptedAtUtc
            };

            _context.Set<Reservation>().Add(reservation);

            try
            {
                await _context.SaveChangesAsync(
                    HttpContext.RequestAborted);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu yêu cầu đặt bàn.");

                ModelState.AddModelError(
                    string.Empty,
                    "Chưa thể gửi yêu cầu lúc này. "
                    + "Vui lòng thử lại sau.");

                return View(input);
            }

            TempData["BookingSuccess"] =
                $"Đã nhận yêu cầu {reservation.Code}. "
                + "Yêu cầu đang chờ nhà hàng xác nhận.";

            return RedirectToRoute("DoraFoodBooking");
        }

        // Kiểm tra quy định và chuẩn bị thời gian để lưu SQL.
        private bool TryPrepareBooking(
     BookingInput input,
     DateTime utcNow,
     out DateTime startsAtUtc,
     out DateTime sessionEndsAtUtc)
        {
            startsAtUtc = default;
            sessionEndsAtUtc = default;

            ModelState.Remove(nameof(BookingInput.EndsAtLocal));
            input.EndsAtLocal = null;

            if (!input.StartsAtLocal.HasValue)
            {
                return false;
            }

            var localNow = GetRestaurantTime(utcNow);

            var arrival = DateTime.SpecifyKind(
                input.StartsAtLocal.Value,
                DateTimeKind.Unspecified);

            if (arrival <= localNow)
            {
                ModelState.AddModelError(
                    nameof(BookingInput.StartsAtLocal),
                    "Giờ đến phải nằm trong tương lai.");
            }

            var arrivalTime = arrival.TimeOfDay;

            var isLunch =
                arrivalTime >= TimeSpan.FromHours(10)
                && arrivalTime < TimeSpan.FromHours(14);

            var isDinner =
                arrivalTime >= TimeSpan.FromHours(16)
                && arrivalTime < TimeSpan.FromHours(22);

            if (!isLunch && !isDinner)
            {
                ModelState.AddModelError(
                    nameof(BookingInput.StartsAtLocal),
                    "Giờ đến phải từ 10:00 đến trước 14:00 "
                    + "hoặc từ 16:00 đến trước 22:00.");
            }
            else
            {
                input.EndsAtLocal = DateTime.SpecifyKind(
                    arrival.Date.AddHours(isLunch ? 14 : 22),
                    DateTimeKind.Unspecified);
            }

            if (!ModelState.IsValid)
            {
                return false;
            }

            startsAtUtc = TimeZoneInfo.ConvertTimeToUtc(
                arrival,
                RestaurantTimeZone);

            sessionEndsAtUtc = TimeZoneInfo.ConvertTimeToUtc(
                input.EndsAtLocal!.Value,
                RestaurantTimeZone);

            return true;
        }

        private static DateTime GetRestaurantTime(DateTime utcNow)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(
                utcNow,
                RestaurantTimeZone);
        }

        // GET: /dat-ban/cua-toi
        [HttpGet(
            "/dat-ban/cua-toi",
            Name = "DoraFoodMyBookings")]
        [HttpGet("/Booking/MyBookings")]
        [Authorize]
        public async Task<IActionResult> MyBookings(int page = 1)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Số trang không hợp lệ.");
            }

            var customerId = await GetActiveCustomerIdAsync();

            if (!customerId.HasValue)
            {
                return await RestartBookingLoginAsync(
                    Url.RouteUrl("DoraFoodMyBookings"));
            }

            const int pageSize = 10;

            var cancellationToken = HttpContext.RequestAborted;

            var query = _context.Set<Reservation>()
                .AsNoTracking()
                .Where(r => r.CustomerId == customerId.Value);

            var totalItems = await query.CountAsync(
                cancellationToken);

            var totalPages = (int)Math.Ceiling(
                totalItems / (double)pageSize);

            page = Math.Clamp(
                page,
                1,
                Math.Max(1, totalPages));

            var reservations = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return View(new MyBookingsViewModel
            {
                Reservations = reservations,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalItems = totalItems
            });
        }

        // POST: /dat-ban/huy
        [HttpPost("/dat-ban/huy")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(long id)
        {
            if (!ModelState.IsValid || id <= 0)
            {
                return BadRequest("Mã yêu cầu không hợp lệ.");
            }

            var customerId = await GetActiveCustomerIdAsync();

            if (!customerId.HasValue)
            {
                return await RestartBookingLoginAsync(
                    Url.RouteUrl("DoraFoodMyBookings"));
            }

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(
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
                        THROW 56021,
                            N'Lịch đặt bàn đang bận. Vui lòng thử lại.',
                            1;
                    """,
                    cancellationToken);

                var now = DateTime.UtcNow;

                var affectedRows = await _context.Set<Reservation>()
                    .Where(r =>
                        r.Id == id
                        && r.CustomerId == customerId.Value
                        && r.Status == "PENDING"
                        && r.StartsAtUtc > now)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            r => r.Status,
                            "CANCELLED"),
                        cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                if (affectedRows == 1)
                {
                    TempData["BookingHistorySuccess"] =
                        "Đã hủy yêu cầu đặt bàn.";
                }
                else
                {
                    TempData["BookingHistoryError"] =
                        "Không thể hủy yêu cầu. "
                        + "Yêu cầu không tồn tại, đã đến giờ hẹn "
                        + "hoặc không còn chờ xác nhận.";
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể hủy yêu cầu đặt bàn {ReservationId}.",
                    id);

                TempData["BookingHistoryError"] =
                    "Chưa thể hủy yêu cầu lúc này. "
                    + "Vui lòng thử lại sau.";
            }

            return RedirectToRoute("DoraFoodMyBookings");
        }

        private async Task<IActionResult> RestartBookingLoginAsync(
            string? returnUrl = null)
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction(
                "Login",
                "Account",
                new
                {
                    returnUrl = returnUrl
                        ?? Url.RouteUrl("DoraFoodBooking")
                });
        }

        private async Task<long?> GetActiveCustomerIdAsync()
        {
            if (User.Identity?.IsAuthenticated != true
                || !long.TryParse(
                    User.FindFirstValue(
                        ClaimTypes.NameIdentifier),
                    out var userId)
                || !Guid.TryParse(
                    User.FindFirstValue("SecurityStamp"),
                    out var securityStamp))
            {
                return null;
            }

            var validAccount = await _context
                .Set<DoraFood.Models.User>()
                .AsNoTracking()
                .AnyAsync(
                    u => u.Id == userId
                        && u.IsActive
                        && u.SecurityStamp == securityStamp,
                    HttpContext.RequestAborted);

            return validAccount ? userId : null;
        }
    }
}
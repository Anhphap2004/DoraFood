using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Controllers
{
    [Authorize(Roles = "ADMIN,MANAGER")]
    public partial class BookingManagementController : Controller
    {
        private readonly DoraFoodContext _context;
        private readonly ILogger<BookingManagementController> _logger;

        private static readonly string[] AllowedStatuses =
        {
            "ALL", "PENDING", "CONFIRMED", "SEATED",
            "COMPLETED", "CANCELLED", "NO_SHOW"
        };

        public BookingManagementController(
            DoraFoodContext context,
            ILogger<BookingManagementController> logger)
        {
            _context = context;
            _logger = logger;
        }

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

            if (!TryGetServiceSessionBounds(
                    reservation.StartsAtUtc, out _, out _))
            {
                TempData["BookingManagementError"] =
                    "Giờ đến phải trong khung phục vụ của nhà hàng.";

                return RedirectToRoute(
                    "DoraFoodManageBookingDetails", new { id });
            }

            var model = await BuildConfirmModelAsync(reservation);

            return View(model);
        }

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

            return await RunBookingTransactionAsync(async () =>
            {
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
                if (!IsCurrentVersion(reservation, input.Version))
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

                    TempData["BookingManagementError"] =
                        "Yêu cầu đã quá hạn giữ bàn và được hủy.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails", new { id });
                }

                if (!TryGetServiceSessionBounds(
                        reservation.StartsAtUtc,
                        out var sessionStartUtc,
                        out var sessionEndUtc))
                {
                    TempData["BookingManagementError"] =
                        "Giờ đến phải trong khung phục vụ của nhà hàng.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails", new { id });
                }

                var tableIds = input.TableIds.Distinct().ToArray();
                var selectedTables = await ValidateTablesAsync(
                    reservation.Id, tableIds, reservation.PartySize,
                    sessionStartUtc, sessionEndUtc, required: true);

                if (selectedTables == null)
                {
                    return View("Confirm",
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

                TempData["BookingManagementSuccess"] =
                    $"Đã xác nhận và phân bàn cho yêu cầu {reservation.Code}.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "CONFIRMED" });
            }, () => RedirectToAction(nameof(Confirm), new { id }), "BookingManagementError");
        }

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

            return await RunBookingTransactionAsync(async () =>
            {
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

                if (!IsCurrentVersion(reservation, input.Version))
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

                    TempData["BookingManagementError"] =
                        "Yêu cầu đã quá hạn giữ bàn và được hủy. "
                        + "Không thể điều chỉnh yêu cầu này.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails",
                        new { id });
                }

                // Giờ đến mới theo giờ Việt Nam, có thể là ngày sau.
                var startsAtLocal = DateTime.SpecifyKind(
                    input.StartsAtLocal!.Value,
                    DateTimeKind.Unspecified);

                var localNow = DateTime.SpecifyKind(
                    now.AddHours(7),
                    DateTimeKind.Unspecified);

                if (startsAtLocal <= localNow)
                {
                    ModelState.AddModelError(
                        nameof(input.StartsAtLocal),
                        "Giờ đến mới phải ở tương lai.");
                }

                DateTime startsAtUtc = default;
                DateTime sessionStartUtc = default;
                DateTime endsAtUtc = default;

                if (ModelState.IsValid)
                {
                    startsAtUtc = new DateTimeOffset(
                        startsAtLocal, TimeSpan.FromHours(7)).UtcDateTime;

                    if (!TryGetServiceSessionBounds(
                            startsAtUtc, out sessionStartUtc, out endsAtUtc))
                    {
                        ModelState.AddModelError(
                            nameof(input.StartsAtLocal),
                            "Giờ đến phải từ 10:00 đến trước 14:00 "
                                + "hoặc từ 16:00 đến trước 22:00.");
                    }
                }

                if (!ModelState.IsValid)
                {
                    return await ShowFormAsync();
                }

                input.EndsAtLocal = endsAtUtc.AddHours(7);

                var tableIds = input.TableIds.Distinct().ToArray();
                var selectedTables = await ValidateTablesAsync(
                    reservation.Id, tableIds, input.PartySize!.Value,
                    sessionStartUtc, endsAtUtc,
                    required: reservation.Status == "CONFIRMED");

                if (selectedTables == null)
                {
                    return await ShowFormAsync();
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

                TempData["BookingManagementSuccess"] =
                    $"Đã điều chỉnh yêu cầu đặt bàn {reservation.Code}.";

                return RedirectToRoute(
                    "DoraFoodManageBookingDetails",
                    new { id });
            }, () => RedirectToRoute("DoraFoodEditBooking", new { id }), "BookingManagementError");
        }

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

            var cancellationToken = HttpContext.RequestAborted;

            return await RunBookingTransactionAsync(async () =>
            {
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

                if (!IsCurrentVersion(reservation, input.Version))
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

                TempData["BookingManagementSuccess"] =
                    $"Đã hủy yêu cầu đặt bàn {reservation.Code}.";

                return BackToDetails();
            }, () => BackToDetails(), "BookingManagementError");
        }

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

            var cancellationToken = HttpContext.RequestAborted;

            return await RunBookingTransactionAsync(async () =>
            {
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

                if (!IsCurrentVersion(reservation, version))
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

                TempData["BookingManagementSuccess"] =
                    $"Đã đánh dấu khách không đến cho yêu cầu {reservation.Code}.";

                return BackToDetails();
            }, () => BackToDetails(), "BookingManagementError");
        }

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

            var cancellationToken = HttpContext.RequestAborted;

            return await RunBookingTransactionAsync(async () =>
            {
                // Dùng cùng khóa với chức năng xác nhận và phân bàn.

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
                if (!IsCurrentVersion(reservation, version))
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

                    TempData["BookingManagementError"] =
                        "Đã quá hạn giữ bàn 30 phút. "
                        + "Yêu cầu đã được hủy, không thể nhận khách.";

                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails",
                        new { id });
                }

                if (reservation.StartsAtUtc.AddHours(7).Date
                    != DateTime.UtcNow.AddHours(7).Date)
                {
                    TempData["BookingManagementError"] =
                        "Chỉ nhận khách vào đúng ngày đã đặt. "
                        + "Nếu khách đổi ngày, hãy điều chỉnh yêu cầu trước.";
                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails", new { id });
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

                TempData["BookingManagementSuccess"] =
                    $"Đã nhận khách cho yêu cầu {reservation.Code}.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "SEATED" });
            }, () => RedirectToRoute("DoraFoodManageBookings", new { status = "CONFIRMED" }), "BookingManagementError");
        }

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

            var cancellationToken = HttpContext.RequestAborted;

            return await RunBookingTransactionAsync(async () =>
            {
                // Dùng cùng khóa với xác nhận bàn và nhận khách.

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
                if (!IsCurrentVersion(reservation, version))
                {
                    TempData["BookingManagementError"] =
                        "Yêu cầu đã được cập nhật. Anh tải lại trang rồi kiểm tra lại.";

                    return RedirectToRoute(
                        "DoraFoodManageBookings",
                        new { status = "SEATED" });
                }

                var hasOpenOrders = await _context.Set<Order>().AnyAsync(
                    order => order.ReservationId == id
                        && (order.Status == "OPEN" || order.Status == "IN_PROGRESS"
                            || order.OrderTables.Any(link => link.ReleasedAtUtc == null)),
                    cancellationToken);

                if (hasOpenOrders)
                {
                    TempData["BookingManagementError"] =
                        "Lượt khách còn đơn chưa đóng hoặc chưa trả bàn. "
                        + "Hãy xử lý đơn gọi món trước khi hoàn tất.";
                    return RedirectToRoute(
                        "DoraFoodManageBookingDetails", new { id });
                }

                // Giữ nguyên liên kết bàn để lưu lịch sử.
                reservation.Status = "COMPLETED";

                // Ghi lịch sử hoàn tất lượt đặt bàn.
                AddBookingHistory(
                    reservation,
                    "BOOKING_COMPLETED",
                    "Hoàn tất lượt đặt bàn sau khi khách rời bàn.");

                await _context.SaveChangesAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã hoàn tất lượt đặt bàn {reservation.Code}.";

                return RedirectToRoute(
                    "DoraFoodManageBookings",
                    new { status = "COMPLETED" });
            }, () => RedirectToRoute("DoraFoodManageBookings", new { status = "SEATED" }), "BookingManagementError");
        }

        [HttpPost(
            "/quan-ly/dat-ban/{id:long}/mo-don",
            Name = "DoraFoodOpenTableOrder")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OpenTableOrder([FromRoute] long id)
        {
            IActionResult BackToDetails()
            {
                return RedirectToRoute(
                    "DoraFoodManageBookingDetails",
                    new { id });
            }

            if (!long.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var staffId))
            {
                return Forbid();
            }

            var cancellationToken = HttpContext.RequestAborted;

            return await RunBookingTransactionAsync(async () =>
            {
                // Dùng chung khóa với nhận khách và hoàn tất lượt đặt bàn.

                var reservation = await _context.Set<Reservation>()
                    .AsNoTracking()
                    .Include(r => r.Tables)
                    .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

                if (reservation == null)
                {
                    return NotFound("Không tìm thấy yêu cầu đặt bàn.");
                }

                if (reservation.Status != "SEATED")
                {
                    TempData["BookingManagementError"] =
                        "Chỉ mở đơn gọi món sau khi khách đã vào bàn.";

                    return BackToDetails();
                }

                var existingOrders = await _context.Set<Order>()
                    .AsNoTracking()
                    .Where(o =>
                        o.ReservationId == id
                        && o.OrderType == "DINE_IN"
                        && (o.Status == "OPEN" || o.Status == "IN_PROGRESS"))
                    .OrderBy(o => o.Id)
                    .Take(2)
                    .ToListAsync(cancellationToken);

                if (existingOrders.Count > 1)
                {
                    TempData["BookingManagementError"] =
                        "Lượt khách có nhiều đơn đang mở. Anh kiểm tra lại dữ liệu.";

                    return BackToDetails();
                }

                if (existingOrders.Count == 1)
                {

                    TempData["BookingManagementSuccess"] =
                        $"Lượt khách đã có đơn {existingOrders[0].Code}.";

                    return RedirectToRoute(
    "DoraFoodTableOrderDetails",
    new { orderId = existingOrders[0].Id });
                }

                var tableIds = reservation.Tables
                    .Select(t => t.Id)
                    .ToArray();

                if (tableIds.Length == 0)
                {
                    TempData["BookingManagementError"] =
                        "Lượt khách chưa có bàn được phân.";

                    return BackToDetails();
                }

                var hasOccupiedTable = await _context.Set<OrderTable>()
                    .AnyAsync(
                        t => tableIds.Contains(t.TableId)
                            && t.ReleasedAtUtc == null,
                        cancellationToken);

                if (hasOccupiedTable)
                {
                    TempData["BookingManagementError"] =
                        "Có bàn đang gắn với đơn khác chưa trả bàn.";

                    return BackToDetails();
                }

                var now = DateTime.UtcNow;

                var order = new Order
                {
                    Code = "DH-" + Guid.NewGuid().ToString("N"),
                    ReservationId = reservation.Id,
                    CustomerId = reservation.CustomerId,
                    OrderType = "DINE_IN",
                    Status = "OPEN",
                    GuestName = reservation.GuestName,
                    GuestPhone = reservation.GuestPhone,
                    CreatedBy = staffId,
                    OpenedAtUtc = now
                };

                foreach (var tableId in tableIds)
                {
                    order.OrderTables.Add(new OrderTable
                    {
                        TableId = tableId,
                        AssignedAtUtc = now
                    });
                }

                _context.Set<Order>().Add(order);

                await _context.SaveChangesAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã mở đơn {order.Code} cho {tableIds.Length} bàn.";

                return RedirectToRoute(
    "DoraFoodTableOrderDetails",
    new { orderId = order.Id });
            }, () => BackToDetails(), "BookingManagementError");
        }

        [HttpGet(
            "/quan-ly/don-goi-mon/{orderId:long}",
            Name = "DoraFoodTableOrderDetails")]
        public async Task<IActionResult> TableOrderDetails(
            [FromRoute] long orderId)
        {

            var order = await _context.Set<Order>()
                .AsNoTracking()
                .Include(o => o.OrderItems)
                .Include(o => o.Reservation)
                .Include(o => o.OrderTables)
                    .ThenInclude(t => t.Table)
                .AsSplitQuery()
                .FirstOrDefaultAsync(
                    o => o.Id == orderId && o.OrderType == "DINE_IN",
                    HttpContext.RequestAborted);

            if (order == null)
            {
                return NotFound("Không tìm thấy đơn gọi món tại bàn.");
            }
            var canAddItems =
                (order.Status == "OPEN" || order.Status == "IN_PROGRESS")
                && order.Reservation?.Status == "SEATED"
                && order.OrderTables.Any(link => link.ReleasedAtUtc == null);

            ViewData["CanAddTableOrderItems"] = canAddItems;

            if (canAddItems)
            {
                ViewData["AvailableDishVariants"] = await _context.Set<DishVariant>()
                    .AsNoTracking()
                    .Include(variant => variant.Dish)
                    .Where(variant => variant.IsAvailable && variant.Dish.IsActive)
                    .OrderBy(variant => variant.Dish.Name)
                    .ThenBy(variant => variant.Name)
                    .ThenBy(variant => variant.Id)
                    .ToListAsync(HttpContext.RequestAborted);
            }
            return View("TableOrderDetails", order);
        }

        [HttpPost(
            "/quan-ly/don-goi-mon/{orderId:long}/them-mon",
            Name = "DoraFoodAddTableOrderItem")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTableOrderItem(
            [FromRoute] long orderId,
            [FromForm] long variantId,
            [FromForm] int quantity,
            [FromForm] string? note)
        {
            IActionResult BackToOrder()
            {
                return RedirectToRoute(
                    "DoraFoodTableOrderDetails",
                    new { orderId });
            }

            note = note?.Trim();

            if (!ModelState.IsValid || variantId <= 0 || quantity < 1
                || quantity > 999 || note?.Length > 500)
            {
                TempData["TableOrderError"] =
                    "Vui lòng chọn món, nhập số lượng từ 1 đến 999 "
                    + "và ghi chú tối đa 500 ký tự.";

                return BackToOrder();
            }

            var cancellationToken = HttpContext.RequestAborted;

            return await RunBookingTransactionAsync(async () =>
            {
                var order = await _context.Set<Order>()
                    .AsNoTracking()
                    .Include(o => o.Reservation)
                    .Include(o => o.OrderTables)
                    .FirstOrDefaultAsync(
                        o => o.Id == orderId && o.OrderType == "DINE_IN",
                        cancellationToken);

                if (order == null)
                {
                    return NotFound("Không tìm thấy đơn gọi món tại bàn.");
                }

                if ((order.Status != "OPEN" && order.Status != "IN_PROGRESS")
                    || order.Reservation?.Status != "SEATED"
                    || !order.OrderTables.Any(link => link.ReleasedAtUtc == null))
                {
                    TempData["TableOrderError"] =
                        "Chỉ thêm món khi đơn đang mở và lượt khách còn sử dụng bàn.";

                    return BackToOrder();
                }

                var variant = await _context.Set<DishVariant>()
                    .AsNoTracking()
                    .Include(v => v.Dish)
                    .FirstOrDefaultAsync(
                        v => v.Id == variantId
                            && v.IsAvailable
                            && v.Dish.IsActive,
                        cancellationToken);

                if (variant == null)
                {
                    TempData["TableOrderError"] =
                        "Món hoặc lựa chọn này hiện không còn bán.";

                    return BackToOrder();
                }

                // NameSnapshot có tối đa 250 ký tự. Giữ đủ tên biến thể.
                var variantSuffix = $" · {variant.Name}";
                var dishName = variant.Dish.Name;
                var maxDishNameLength = 250 - variantSuffix.Length;

                if (dishName.Length > maxDishNameLength)
                {
                    dishName = dishName[..maxDishNameLength].TrimEnd();
                }

                _context.Set<OrderItem>().Add(new OrderItem
                {
                    OrderId = order.Id,
                    VariantId = variant.Id,
                    NameSnapshot = dishName + variantSuffix,
                    Quantity = quantity,
                    UnitPrice = variant.Price,
                    Status = "NEW",
                    Note = string.IsNullOrWhiteSpace(note) ? null : note,
                    CreatedAtUtc = DateTime.UtcNow
                });

                await _context.SaveChangesAsync(cancellationToken);

                TempData["BookingManagementSuccess"] =
                    $"Đã thêm {quantity} × {variant.Dish.Name}.";

                return BackToOrder();
            }, () => BackToOrder(), "TableOrderError");
        }
    }
}

using System;
using System.Data;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AccountUser = DoraFood.Models.User;

namespace DoraFood.Controllers;

[Authorize(Roles = "WAITER,MANAGER,CASHIER")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("phuc-vu")]
public sealed partial class StaffController : Controller
{
    private readonly DoraFoodContext _db;
    private readonly ILogger<StaffController> _logger;

    private bool IsAdminTableBoard => ViewData["AdminTableBoard"] is true;
    private string TableBoardRoute => IsAdminTableBoard ? "DoraFoodAdminTableService" : "DoraFoodStaffDesk";
    private string TableOrderRoute => IsAdminTableBoard ? "DoraFoodAdminTableOrder" : "DoraFoodStaffOrder";

    public StaffController(DoraFoodContext db, ILogger<StaffController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var stampClaim = User.FindFirst("SecurityStamp")?.Value;

        if (!long.TryParse(userIdClaim, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            || userId <= 0 || !Guid.TryParse(stampClaim, out var stamp))
        {
            context.Result = Forbid();
            return;
        }

        var waiter = User.IsInRole("WAITER");
        var manager = User.IsInRole("MANAGER");
        var cashier = User.IsInRole("CASHIER");
        var account = await _db.Set<AccountUser>().AsNoTracking()
            .Where(item => item.Id == userId && item.IsActive && item.SecurityStamp == stamp)
            .Select(item => new
            {
                CanServe = waiter && item.Roles.Any(role => role.Code == "WAITER"),
                CanCollect = item.Roles.Any(role =>
                    (manager && role.Code == "MANAGER") || (cashier && role.Code == "CASHIER"))
            })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        var action = context.ActionDescriptor
            as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor;
        var isAdminBoard = action?.MethodInfo.Name is nameof(AdminBoard) or nameof(AdminOrder);
        if (account is null || (!account.CanCollect && (!account.CanServe || isAdminBoard)))
        {
            context.Result = Forbid();
            return;
        }

        _actorId = userId;
        ViewData["CanCollectTablePayments"] = account.CanCollect;
        // Các form dùng chung chuyển ngữ cảnh từ URL. Quyền quản trị vẫn do tài khoản quyết định.
        var fromAdminBoard = bool.TryParse(Request.Query["adminBoard"].ToString(), out var requested) && requested;
        ViewData["AdminTableBoard"] = account.CanCollect && (isAdminBoard || fromAdminBoard);
        await next();
    }

    [HttpGet("", Name = "DoraFoodStaffDesk")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await LoadTablesAsync(cancellationToken));

    [HttpGet("/admin/phuc-vu-ban", Name = "DoraFoodAdminTableService")]
    [Authorize(Roles = "MANAGER,CASHIER")]
    public async Task<IActionResult> AdminBoard(CancellationToken cancellationToken)
    {
        return View("Index", await LoadTablesAsync(cancellationToken));
    }

    private async Task<IReadOnlyList<StaffTableRow>> LoadTablesAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var heldReservations = HeldReservationsForServiceDay(now);
        var tables = await _db.Set<DiningTable>().AsNoTracking()
            .Where(table => table.IsActive && table.Area.IsActive)
            .OrderBy(table => table.Area.Name).ThenBy(table => table.Code).ThenBy(table => table.Id)
            .Select(table => new
            {
                table.Id,
                table.Code,
                AreaName = table.Area.Name,
                table.Capacity,
                HasReservation = table.Reservations.Any(reservation => reservation.Status == "SEATED")
                    || heldReservations.Any(reservation => reservation.Tables.Any(assigned => assigned.Id == table.Id)),
                SeatedReservationId = table.Reservations
                    .Where(reservation => reservation.Status == "SEATED")
                    .OrderBy(reservation => reservation.Id)
                    .Select(reservation => (long?)reservation.Id).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // Chỉ các liên kết bàn chưa giải phóng mới quyết định trạng thái đang phục vụ.
        var links = await _db.Set<OrderTable>().AsNoTracking()
            .Where(link => link.ReleasedAtUtc == null)
            .Select(link => new
            {
                link.TableId,
                link.OrderId,
                IsOpen = link.Order.OrderType == "DINE_IN"
                    && (link.Order.Status == "OPEN" || link.Order.Status == "IN_PROGRESS")
                    && link.Order.ClosedAtUtc == null
                    && (link.Order.ReservationId == null || link.Order.Reservation!.Status == "SEATED"),
                HasItems = link.Order.OrderItems.Any(item => item.Status != "CANCELLED")
            })
            .ToDictionaryAsync(link => link.TableId, cancellationToken);

        var upcomingOrders = await (
            from booking in heldReservations
            from table in booking.Tables
            from order in booking.Orders
            where order.OrderType == "DINE_IN" && order.ClosedAtUtc == null
                && (order.Status == "OPEN" || order.Status == "IN_PROGRESS")
                && order.Invoice == null
                && order.OrderItems.Any(line => line.Status != "CANCELLED")
            select new { TableId = table.Id, BookingId = booking.Id, booking.StartsAtUtc })
            .ToListAsync(cancellationToken);

        var upcomingByTable = upcomingOrders
            .Distinct()
            .OrderBy(booking => booking.StartsAtUtc).ThenBy(booking => booking.BookingId)
            .ToLookup(booking => booking.TableId,
                booking => new StaffUpcomingBooking(booking.BookingId, booking.StartsAtUtc));

        return tables.Select(table =>
        {
            links.TryGetValue(table.Id, out var link);
            var orderId = link is { IsOpen: true } ? (long?)link.OrderId : null;
            return new StaffTableRow(table.Id, table.Code, table.AreaName, table.Capacity,
                table.HasReservation || link is not null,
                link is null ? table.SeatedReservationId : null,
                orderId, link is { IsOpen: true, HasItems: true })
            {
                UpcomingBookings = upcomingByTable[table.Id].ToList()
            };
        }).ToList();
    }

    private IQueryable<Reservation> HeldReservationsForServiceDay(DateTime now)
    {
        var dayEndUtc = now.AddHours(7).Date.AddDays(1).AddHours(-7);
        var arrivalCutoff = now.AddMinutes(-30);
        return _db.Set<Reservation>().AsNoTracking().Where(booking =>
            booking.Status == "CONFIRMED" && booking.StartsAtUtc > arrivalCutoff
            && booking.StartsAtUtc < dayEndUtc && booking.EndsAtUtc > now);
    }

    [HttpPost("dang-xuat", Name = "DoraFoodStaffLogout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect(Url.Content("~/"));
    }


    [HttpGet("/admin/thanh-toan-tai-ban", Name = "DoraFoodAdminTablePayments")]
    [Authorize(Roles = "MANAGER,CASHIER")]
    public async Task<IActionResult> AdminTablePayments(
    CancellationToken cancellationToken)
    {
        if (ViewData["CanCollectTablePayments"] is not true)
        {
            return Forbid();
        }

        var orders = await OpenTableOrders()
            .Include(order => order.OrderTables
                .Where(link => link.ReleasedAtUtc == null))
                .ThenInclude(link => link.Table)
            .OrderByDescending(order => order.Id)
            .ToListAsync(cancellationToken);

        return View("TablePayments", orders);
    }

    [HttpGet("/admin/phuc-vu-ban/don/{id:long}", Name = "DoraFoodAdminTableOrder")]
    [Authorize(Roles = "MANAGER,CASHIER")]
    public Task<IActionResult> AdminOrder([FromRoute] long id, CancellationToken cancellationToken) =>
        Details(id, cancellationToken);

    [HttpGet("don/{id:long}", Name = "DoraFoodStaffOrder")]
    public async Task<IActionResult> Details(
        [FromRoute] long id, CancellationToken cancellationToken)
    {
        var order = await OpenTableOrders()
            .Include(item => item.OrderTables.Where(link => link.ReleasedAtUtc == null))
                .ThenInclude(link => link.Table)
            .Include(item => item.OrderItems.Where(line => line.Status != "CANCELLED"))
                .ThenInclude(line => line.Variant)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound("Không tìm thấy đơn tại bàn đang phục vụ.");
        }

        var options = await AvailableDishes()
            .OrderBy(item => item.Dish.Name)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new StaffDishOption(
                item.Id, item.Dish.Name + " · " + item.Name, item.Price,
                _db.Set<DishImage>().Where(image => image.DishId == item.DishId)
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder).ThenBy(image => image.Id)
                    .Select(image => image.MediaFile.Url).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var orderedDishIds = order.OrderItems.Select(line => line.Variant.DishId).Distinct().ToArray();
        var photos = await _db.Set<DishImage>().AsNoTracking()
            .Where(image => orderedDishIds.Contains(image.DishId))
            .OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => new { image.DishId, image.MediaFile.Url })
            .ToListAsync(cancellationToken);
        var orderedPhotos = photos.GroupBy(image => image.DishId)
            .ToDictionary(group => group.Key, group => group.First().Url);

        return View("Details", new StaffOrderDetails(
            order.Id, order.Code,
            order.OrderTables.Select(link => link.Table.Code).OrderBy(code => code).ToList(),
            order.OrderItems.OrderBy(item => item.Id)
                .Select(item => new StaffOrderLine(
                    item.Id, item.NameSnapshot, item.Quantity, item.UnitPrice, item.Note, item.Status,
                    orderedPhotos.GetValueOrDefault(item.Variant.DishId), item.CreatedAtUtc))
                .ToList(),
            options, await LoadStaffOperationsAsync(order.Id, cancellationToken),
            order.OrderItems.Count == 0
                && await CanReleaseEmptyOrderAsync(order, cancellationToken)));
    }

    [HttpPost("don/{id:long}/them-mon", Name = "DoraFoodStaffAddDish")]
    public async Task<IActionResult> AddDish(
        [FromRoute] long id, [FromForm] long variantId, [FromForm] int quantity,
        [FromForm] string? note, [FromForm] Guid operationId, CancellationToken cancellationToken)
    {
        note = note?.Trim();
        if (!ModelState.IsValid || variantId <= 0 || operationId == Guid.Empty
            || quantity is < 1 or > 999 || note?.Length > 500)
        {
            TempData["StaffError"] = "Chọn món, nhập số lượng từ 1 đến 999 và ghi chú tối đa 500 ký tự.";
            return RedirectToRoute(TableOrderRoute, new { id });
        }

        var actorId = _actorId;
        var orderKey = id.ToString(CultureInfo.InvariantCulture);
        const string auditAction = "staff.order.add-item";

        try
        {
            var outcome = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var transaction = await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

                // Cùng khóa với các thao tác tại bàn của quản lý.
                await _db.Database.ExecuteSqlRawAsync(TableLockSql, cancellationToken);

                if (await _db.Set<AuditLog>().AnyAsync(log => log.ActorId == actorId
                    && log.Action == auditAction && log.EntityName == "Orders"
                    && log.EntityKey == orderKey && log.CorrelationId == operationId,
                    cancellationToken))
                {
                    return "added";
                }

                if (!await OpenTableOrders().AnyAsync(order => order.Id == id, cancellationToken))
                {
                    return "closed";
                }

                var variant = await AvailableDishes()
                    .Where(item => item.Id == variantId)
                    .Select(item => new { item.Id, item.Price, Dish = item.Dish.Name, item.Name })
                    .SingleOrDefaultAsync(cancellationToken);

                if (variant is null)
                {
                    return "unavailable";
                }

                var name = $"{variant.Dish} · {variant.Name}";
                _db.Set<OrderItem>().Add(new OrderItem
                {
                    OrderId = id,
                    VariantId = variant.Id,
                    NameSnapshot = name.Length > 250 ? name[..250] : name,
                    Quantity = quantity,
                    UnitPrice = variant.Price,
                    Status = "NEW",
                    Note = string.IsNullOrWhiteSpace(note) ? null : note,
                    CreatedAtUtc = DateTime.UtcNow
                });

                _db.Set<AuditLog>().Add(new AuditLog
                {
                    ActorId = actorId,
                    Action = auditAction,
                    EntityName = "Orders",
                    EntityKey = orderKey,
                    CorrelationId = operationId,
                    Summary = $"Đã thêm {quantity} × {name}.",
                    CreatedAtUtc = DateTime.UtcNow
                });

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return "added";
            });

            if (outcome == "closed")
            {
                TempData["StaffError"] = "Đơn đã đóng hoặc bàn không còn phục vụ.";
                return RedirectToRoute(TableBoardRoute);
            }

            if (outcome == "unavailable")
            {
                TempData["StaffError"] = "Món này hiện không còn bán.";
            }
            else
            {
                TempData["StaffSuccess"] = "Đã thêm món vào đơn.";
            }
        }
        catch (Exception ex) when (ex is DbUpdateException or SqlException)
        {
            _logger.LogWarning(ex, "Không lưu được món cho đơn phục vụ {OrderId}.", id);
            TempData["StaffError"] = "Chưa xác nhận được thao tác. Kiểm tra danh sách món trước khi thử lại.";
        }

        return RedirectToRoute(TableOrderRoute, new { id });
    }

    private IQueryable<Order> OpenTableOrders() =>
        _db.Set<Order>().AsNoTracking().Where(order =>
            order.OrderType == "DINE_IN"
            && (order.Status == "OPEN" || order.Status == "IN_PROGRESS")
            && order.ClosedAtUtc == null
            && (order.ReservationId == null || order.Reservation!.Status == "SEATED")
            && order.OrderTables.Any(link => link.ReleasedAtUtc == null
                && link.Table.IsActive && link.Table.Area.IsActive));

    private IQueryable<DishVariant> AvailableDishes() =>
        _db.Set<DishVariant>().AsNoTracking().Where(item =>
            item.IsAvailable && item.Dish.IsActive && item.Price >= 0);
}

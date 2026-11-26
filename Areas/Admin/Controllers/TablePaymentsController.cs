using System;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DoraFood.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER,CASHIER")]
public partial class TablePaymentsController : Controller
{
    private readonly DoraFoodContext _context;
    private readonly ILogger<TablePaymentsController> _logger;
    private readonly NotificationService _notifications;

    public TablePaymentsController(
        DoraFoodContext context,
        ILogger<TablePaymentsController> logger,
        NotificationService notifications)
    {
        _context = context;
        _logger = logger;
        _notifications = notifications;
    }

    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(User.FindFirstValue("SecurityStamp"), out var stamp))
        {
            context.Result = Forbid();
            return;
        }

        var canCollectAsManager = User.IsInRole("MANAGER");
        var canCollectAsCashier = User.IsInRole("CASHIER");

        var canViewAsAdmin = User.IsInRole("ADMIN")
            && context.ActionDescriptor is
                Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor action
            && action.MethodInfo.Name == nameof(TableInvoice);

        var allowed = await _context.Set<User>()
            .AsNoTracking()
            .AnyAsync(user =>
                user.Id == userId
                && user.IsActive
                && user.SecurityStamp == stamp
                && user.Roles.Any(role =>
                    (canCollectAsManager && role.Code == "MANAGER")
                    || (canCollectAsCashier && role.Code == "CASHIER")
                    || (canViewAsAdmin && role.Code == "ADMIN")),
                HttpContext.RequestAborted);

        if (!allowed)
        {
            context.Result = Forbid();
            return;
        }

        await next();
    }

    // Dán RunBookingTransactionAsync vào đây.
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
            _logger.LogError(
                ex,
                "Lỗi lưu dữ liệu tại {Path}",
                Request.Path);

            var environment = HttpContext.RequestServices
                .GetRequiredService<
                    Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();

            // Chỉ hiện lỗi chi tiết khi chạy môi trường Development.
            if (environment.EnvironmentName == "Development")
            {
                var originalError = ex.GetBaseException();

                TempData[errorKey] =
                    originalError is Microsoft.Data.SqlClient.SqlException sqlError
                        ? $"SQL {sqlError.Number}: {sqlError.Message}"
                        : $"Lỗi lưu dữ liệu: {originalError.Message}";
            }
            else
            {
                TempData[errorKey] =
                    "Chưa thể lưu thanh toán. " +
                    "Vui lòng kiểm tra lại hoặc liên hệ quản trị.";
            }
        }
        // Không hiển thị thành công nếu giao dịch không hoàn tất.
        TempData.Remove("BookingManagementSuccess");
        TempData.Remove("TableOrderSuccess");
        return onError();
    }
}
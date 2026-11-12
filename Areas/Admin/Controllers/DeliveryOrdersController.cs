using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DeliveryOrdersController : Controller
{
    private readonly DoraFoodContext _context;

    public DeliveryOrdersController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return BadRequest("Số trang không hợp lệ.");

        const int pageSize = 20;

        // Lấy các khoản thanh toán tiền mặt cho đơn hàng.
        var cashPayments = CashSettlements();

        // Chỉ lấy đơn giao hàng có khoản thanh toán tiền mặt.
        var deliveryOrders = _context.Set<Order>()
            .AsNoTracking()
            .Where(order => order.OrderType == "DELIVERY"
                && cashPayments.Any(payment => payment.OrderId == order.Id));

        var totalOrders = await deliveryOrders.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalOrders / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var orders = await deliveryOrders
            .OrderByDescending(order => order.OpenedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => new DeliveryOrderRow(
                order.Code,
                order.GuestName,
                order.GuestPhone,
                order.DeliveryAddress,
                order.OpenedAtUtc,
                order.Status,
                cashPayments
                    .Where(payment => payment.OrderId == order.Id
                        && payment.Status == "PENDING")
                    .Sum(payment => (decimal?)payment.Amount) ?? 0m,
                cashPayments
                    .Where(payment => payment.OrderId == order.Id
                        && payment.Status == "SUCCEEDED")
                    .Sum(payment => (decimal?)payment.Amount) ?? 0m))
            .ToListAsync(cancellationToken);

        return View(new DeliveryOrderListViewModel(
            orders, page, totalPages, totalOrders));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewCancellation(
    [FromForm] string? code,
    [FromForm] long requestId,
    [FromForm] string? version,
    [FromForm] string? decision,
    [FromForm] string? reviewNote,
    [FromForm] int page = 1,
    CancellationToken cancellationToken = default)
    {
        // 1. Kiểm tra thông tin gửi lên.
        if (!ModelState.IsValid
            || !ValidCode(code)
            || requestId <= 0)
        {
            return BadRequest("Thông tin yêu cầu không hợp lệ.");
        }

        code = code!.Trim();
        reviewNote = reviewNote?.Trim();

        if (string.IsNullOrWhiteSpace(reviewNote))
        {
            reviewNote = null;
        }

        if (decision != "APPROVED" && decision != "REJECTED")
        {
            return BadRequest("Thao tác xét duyệt không hợp lệ.");
        }

        var approve = decision == "APPROVED";

        // Khi từ chối, quản lý phải giải thích cho khách.
        if ((!approve && reviewNote is null)
            || reviewNote?.Length > 500)
        {
            TempData["DeliveryError"] =
                "Khi từ chối phải nhập phản hồi; phản hồi tối đa 500 ký tự.";

            return RedirectToAction(
                nameof(Details),
                new { area = "Admin", code, page });
        }

        // 2. Đọc phiên bản yêu cầu từ form.
        byte[] expectedVersion;

        try
        {
            expectedVersion = Convert.FromBase64String(version ?? "");
        }
        catch (FormatException)
        {
            return BadRequest(
                "Mã phiên bản không hợp lệ. Hãy tải lại trang.");
        }

        if (expectedVersion.Length != 8)
        {
            return BadRequest(
                "Mã phiên bản không hợp lệ. Hãy tải lại trang.");
        }

        // Kiểm tra tài khoản quản lý vẫn đang hoạt động và có quyền.
        var actorId = await GetActorIdAsync(cancellationToken);

        if (!actorId.HasValue)
        {
            return Forbid();
        }

        var userId = actorId.Value;
        string? error;

        try
        {
            error = await _context.Database
                .CreateExecutionStrategy()
                .ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();

                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            IsolationLevel.Serializable,
                            cancellationToken);

                    // 3. Khóa đơn trước khi kiểm tra và thay đổi.
                    // Các thao tác cùng đơn phải chờ giao dịch này hoàn tất.
                    var order = await _context.Set<Order>()
                        .FromSqlInterpolated($@"
                        SELECT *
                        FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
                        WHERE Code = {code}
                          AND OrderType = 'DELIVERY'")
                        .AsNoTracking()
                        .SingleOrDefaultAsync(cancellationToken);

                    if (order is null)
                    {
                        return "Không tìm thấy đơn giao hàng.";
                    }

                    var request = await _context
                        .Set<OrderCancellationRequest>()
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            r => r.Id == requestId
                                && r.OrderId == order.Id,
                            cancellationToken);

                    if (request is null)
                    {
                        return "Yêu cầu hủy không thuộc đơn này.";
                    }

                    if (request.Status != "PENDING"
                        || !request.Version.SequenceEqual(expectedVersion))
                    {
                        return
                            "Yêu cầu đã thay đổi hoặc đã được xử lý. Hãy tải lại trang.";
                    }

                    // Lấy giờ từ SQL Server, thống nhất với thời gian tạo yêu cầu.
                    var now = await _context.Database
                        .SqlQueryRaw<DateTime>(
                            "SELECT SYSUTCDATETIME() AS [Value]")
                        .SingleAsync(cancellationToken);

                    // 4. Nếu đồng ý: kiểm tra và hủy đơn.
                    if (approve)
                    {
                        if (order.ClosedAtUtc.HasValue
                            || (order.Status != "OPEN"
                                && order.Status != "IN_PROGRESS"))
                        {
                            return
                                "Chỉ được chấp thuận hủy đơn đang chờ hoặc đang chuẩn bị.";
                        }

                        var payments = _context.Set<Payment>()
                            .Where(p => p.OrderId == order.Id);

                        var hasCollectedPayment = await payments
                            .AnyAsync(
                                p => p.Status == "SUCCEEDED",
                                cancellationToken);

                        if (hasCollectedPayment)
                        {
                            return
                                "Đơn đã thu tiền, không thể hủy theo quy trình này.";
                        }

                        var pendingPayments = await (
                            from payment in payments.AsNoTracking()
                            join method in _context.Set<PaymentMethod>()
                                .AsNoTracking()
                                on payment.MethodId equals method.Id
                            where payment.Status == "PENDING"
                            select new
                            {
                                payment.Id,
                                payment.Amount,
                                payment.Purpose,
                                MethodCode = method.Code
                            })
                            .Take(2)
                            .ToListAsync(cancellationToken);

                        if (pendingPayments.Count != 1
                            || pendingPayments[0].MethodCode != "CASH"
                            || pendingPayments[0].Purpose != "SETTLEMENT"
                            || pendingPayments[0].Amount <= 0m)
                        {
                            return
                                "Khoản tiền mặt chờ thu không hợp lệ. Hãy kiểm tra lại thanh toán.";
                        }

                        // Hủy đơn.
                        var changedOrders = await _context.Set<Order>()
                            .Where(o =>
                                o.Id == order.Id
                                && o.ClosedAtUtc == null
                                && (o.Status == "OPEN"
                                    || o.Status == "IN_PROGRESS"))
                            .ExecuteUpdateAsync(
                                setters => setters
                                    .SetProperty(
                                        o => o.Status,
                                        "CANCELLED")
                                    .SetProperty(
                                        o => o.ClosedAtUtc,
                                        (DateTime?)now),
                                cancellationToken);

                        if (changedOrders != 1)
                        {
                            return "Đơn vừa thay đổi. Hãy tải lại trang.";
                        }

                        // Hủy khoản tiền mặt chờ thu.
                        var paymentId = pendingPayments[0].Id;

                        var changedPayments = await payments
                            .Where(p =>
                                p.Id == paymentId
                                && p.Status == "PENDING")
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(
                                    p => p.Status,
                                    "CANCELLED"),
                                cancellationToken);

                        if (changedPayments != 1)
                        {
                            return
                                "Khoản chờ thu vừa thay đổi. Hãy tải lại trang.";
                        }

                        // Hủy các món thuộc đơn.
                        await _context.Set<OrderItem>()
                            .Where(item =>
                                item.OrderId == order.Id
                                && item.Status != "CANCELLED")
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(
                                    item => item.Status,
                                    "CANCELLED"),
                                cancellationToken);
                    }

                    // 5. Lưu kết quả xét duyệt.
                    // Khi từ chối, chỉ thay đổi yêu cầu hủy.
                    var changedRequests = await _context
                        .Set<OrderCancellationRequest>()
                        .Where(r =>
                            r.Id == requestId
                            && r.OrderId == order.Id
                            && r.Status == "PENDING"
                            && r.Version == expectedVersion)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(
                                    r => r.Status,
                                    decision!)
                                .SetProperty(
                                    r => r.ReviewedBy,
                                    (long?)userId)
                                .SetProperty(
                                    r => r.ReviewedAtUtc,
                                    (DateTime?)now)
                                .SetProperty(
                                    r => r.ReviewNote,
                                    reviewNote),
                            cancellationToken);

                    if (changedRequests != 1)
                    {
                        return
                            "Yêu cầu đã được xử lý ở nơi khác. Hãy tải lại trang.";
                    }

                    // 6. Ghi lịch sử thao tác.
                    _context.Set<AuditLog>().Add(new AuditLog
                    {
                        ActorId = userId,

                        Action = approve
                            ? "delivery.cancel"
                            : "delivery.cancel_request.reject",

                        EntityName = "Orders",

                        EntityKey = order.Id.ToString(
                            CultureInfo.InvariantCulture),

                        Summary =
                            $"Yêu cầu hủy #{request.Id}. Khách: {request.Reason}"
                            + (reviewNote is null
                                ? ""
                                : $"\nPhản hồi: {reviewNote}"),

                        CreatedAtUtc = now
                    });

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return (string?)null;
                });
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            error =
                "Đơn đang được xử lý đồng thời. Hãy tải lại trang rồi thử lại.";
        }

        // 7. Thông báo và trở về chi tiết đơn.
        TempData[error is null ? "DeliverySuccess" : "DeliveryError"] =
            error ?? (approve
                ? "Đã đồng ý hủy đơn theo yêu cầu của khách."
                : "Đã từ chối yêu cầu hủy và lưu phản hồi cho khách.");

        return RedirectToAction(
            nameof(Details),
            new
            {
                area = "Admin",
                code,
                page = Math.Max(1, page)
            });
    }
    [HttpGet]
    public async Task<IActionResult> Details(
        string? code,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !ValidCode(code))
        {
            return NotFound();
        }

        code = code!.Trim();

        var cashPayments = CashSettlements();

        var order = await _context.Set<Order>()
            .AsNoTracking()
            .Where(order => order.Code == code
                && order.OrderType == "DELIVERY"
                && cashPayments.Any(payment => payment.OrderId == order.Id))
            .Select(order => new
            {
                order.Id,
                order.Code,
                order.GuestName,
                order.GuestPhone,
                order.DeliveryAddress,
                order.Note,
                order.OpenedAtUtc,
                order.Status,
                PendingAmount = cashPayments
                    .Where(payment => payment.OrderId == order.Id
                        && payment.Status == "PENDING")
                    .Sum(payment => (decimal?)payment.Amount) ?? 0m,
                ReceivedAmount = cashPayments
                    .Where(payment => payment.OrderId == order.Id
                        && payment.Status == "SUCCEEDED")
                    .Sum(payment => (decimal?)payment.Amount) ?? 0m
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order == null)
        {
            return NotFound();
        }

        var items = await _context.Set<OrderItem>()
            .AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .OrderBy(item => item.Id)
            .Select(item => new DeliveryOrderItemRow(
                item.NameSnapshot,
                item.Quantity,
                item.UnitPrice,
                item.Status))
            .ToListAsync(cancellationToken);

        var model = new DeliveryOrderDetailsViewModel(
            order.Code,
            order.GuestName,
            order.GuestPhone,
            order.DeliveryAddress,
            order.Note,
            order.OpenedAtUtc,
            order.Status,
            order.PendingAmount,
            order.ReceivedAmount,
            items,
            Math.Max(1, page));

        if (order.Status == "CANCELLED")
        {
            var entityKey = order.Id.ToString(CultureInfo.InvariantCulture);
            ViewData["DeliveryCancellation"] = await _context.Set<AuditLog>()
                .AsNoTracking()
                .Where(log => log.Action == "delivery.cancel"
                    && log.EntityName == "Orders" && log.EntityKey == entityKey)
                .OrderByDescending(log => log.CreatedAtUtc).ThenByDescending(log => log.Id)
                .Select(log => new DeliveryCancellationInfo(
                    log.Actor != null ? log.Actor.UserName : "Tài khoản không còn",
                    log.Summary ?? "", log.CreatedAtUtc))
                .FirstOrDefaultAsync(cancellationToken);
        }
        // Tìm yêu cầu hủy đang chờ xử lý của đơn.
        var pendingCancellation = await _context
            .Set<OrderCancellationRequest>()
            .AsNoTracking()
            .Where(request =>
                request.OrderId == order.Id
                && request.Status == "PENDING")
            .Select(request => new
            {
                request.Id,
                request.Version,
                CustomerName = request.Requester.UserName,
                request.Reason,
                request.RequestedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        ViewData["PendingCancellation"] =
            pendingCancellation is null
                ? null
                : new CancellationReviewViewModel(
                    pendingCancellation.Id,
                    Convert.ToBase64String(pendingCancellation.Version),
                    order.Code,
                    Math.Max(1, page),
                    pendingCancellation.CustomerName,
                    pendingCancellation.Reason,
                    pendingCancellation.RequestedAtUtc);
        return View(model);
    }
    // OPEN -> IN_PROGRESS: nhà hàng tiếp nhận và chuẩn bị món.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Accept(
        [FromForm] string? code,
        [FromForm] int page = 1,
        CancellationToken cancellationToken = default) =>
        ChangeDeliveryAsync(code, page, "OPEN", "IN_PROGRESS",
            "Đã tiếp nhận đơn. Nhà hàng bắt đầu chuẩn bị món.", cancellationToken);

    // IN_PROGRESS -> SHIPPING: bàn giao món cho người giao hàng.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> StartShipping(
        [FromForm] string? code,
        [FromForm] int page = 1,
        CancellationToken cancellationToken = default) =>
        ChangeDeliveryAsync(code, page, "IN_PROGRESS", "SHIPPING",
            "Đơn đang được giao đến khách.", cancellationToken);

    // SHIPPING -> COMPLETED: chỉ bấm khi đã giao hàng và thu đủ tiền mặt.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CompleteCashDelivery(
        [FromForm] string? code,
        [FromForm] int page = 1,
        CancellationToken cancellationToken = default) =>
        ChangeDeliveryAsync(code, page, "SHIPPING", "COMPLETED",
            "Đã xác nhận giao hàng và thu tiền mặt thành công.", cancellationToken);

    // Dùng chung kiểm tra, giao dịch và thông báo cho ba thao tác trên.
    private async Task<IActionResult> ChangeDeliveryAsync(
        string? code, int page, string fromStatus, string toStatus,
        string successMessage, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !ValidCode(code))
            return BadRequest("Mã đơn hoặc số trang không hợp lệ.");

        code = code!.Trim();
        var actorId = await GetActorIdAsync(cancellationToken);
        if (!actorId.HasValue) return Forbid();
        var userId = actorId.Value;

        string? error;
        try
        {
            // Phù hợp cả khi kết nối SQL đã bật EnableRetryOnFailure.
            error = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // Giữ ổn định các khoản thanh toán đã đọc đến khi commit.
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

                var now = DateTime.UtcNow;
                var completing = toStatus == "COMPLETED";
                var orders = _context.Set<Order>()
                    .Where(order => order.Code == code && order.OrderType == "DELIVERY");

                // Cập nhật có điều kiện: một đơn chỉ đi tiếp từ đúng trạng thái trước đó.
                var updatedOrders = await orders
                    .Where(order => order.Status == fromStatus && order.ClosedAtUtc == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(order => order.Status, toStatus)
                        .SetProperty(order => order.ClosedAtUtc,
                            completing ? (DateTime?)now : null), cancellationToken);

                if (updatedOrders != 1)
                    return "Đơn không còn ở bước phù hợp. Hãy tải lại chi tiết đơn để kiểm tra.";

                if (toStatus == "SHIPPING"
    && await _context.Set<OrderCancellationRequest>()
        .AnyAsync(
            request => request.Order.Code == code
                && request.Status == "PENDING",
            cancellationToken))
                {
                    return "Khách đang yêu cầu hủy đơn. Hãy xử lý yêu cầu trước khi bắt đầu giao.";
                }

                // Cả ba bước đều yêu cầu đúng một khoản thanh toán đang có hiệu lực:
                // tiền mặt, mục đích thanh toán đơn và đang chờ thu.
                // Bản ghi FAILED/CANCELLED của các lần thử cũ không được tính.
                var settlements = await (
                    from payment in _context.Set<Payment>().AsNoTracking()
                    join order in orders on payment.OrderId equals order.Id
                    join method in _context.Set<PaymentMethod>().AsNoTracking()
                        on payment.MethodId equals method.Id
                    where payment.Purpose == "SETTLEMENT"
                        && (payment.Status == "PENDING" || payment.Status == "SUCCEEDED")
                    select new
                    {
                        payment.Id,
                        payment.OrderId,
                        payment.MethodId,
                        payment.Amount,
                        payment.Status,
                        MethodCode = method.Code
                    })
                    .Take(2)
                    .ToListAsync(cancellationToken);

                if (settlements.Count != 1
                    || settlements[0].MethodCode != "CASH"
                    || settlements[0].Status != "PENDING"
                    || settlements[0].Amount <= 0m)
                    return "Đơn phải có đúng một khoản tiền mặt đang chờ thu hợp lệ. Hãy kiểm tra thanh toán.";

                if (completing)
                {
                    var paymentToCollect = settlements[0];
                    var updatedPayments = await _context.Set<Payment>()
                        .Where(payment => payment.Id == paymentToCollect.Id
                            && payment.OrderId == paymentToCollect.OrderId
                            && payment.MethodId == paymentToCollect.MethodId
                            && payment.Purpose == "SETTLEMENT"
                            && payment.Status == "PENDING"
                            && payment.Amount == paymentToCollect.Amount)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(payment => payment.Status, "SUCCEEDED")
                            .SetProperty(payment => payment.PaidAtUtc, (DateTime?)now)
                            .SetProperty(payment => payment.ReceivedBy, (long?)userId),
                            cancellationToken);

                    if (updatedPayments != 1)
                        return "Khoản thu vừa thay đổi. Hãy tải lại chi tiết đơn để kiểm tra.";
                }

                await transaction.CommitAsync(cancellationToken);
                return (string?)null;
                // Nếu return lỗi ở phía trên, DisposeAsync hoàn tác giao dịch chưa commit.
            });
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            error = "Đơn đang được xử lý đồng thời. Hãy tải lại chi tiết rồi thử lại.";
        }

        TempData[error is null ? "DeliverySuccess" : "DeliveryError"] =
            error ?? $"{successMessage} Mã đơn: {code}.";

        return RedirectToAction(nameof(Details),
            new { area = "Admin", code, page = Math.Max(1, page) });
    }

    // Hủy đơn chưa giao và chưa thu tiền; không xóa đơn hoặc dòng món.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        [FromForm] string? code,
        [FromForm] string? reason,
        [FromForm] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !ValidCode(code))
            return BadRequest("Mã đơn hoặc số trang không hợp lệ.");

        code = code!.Trim();
        reason = reason?.Trim() ?? "";
        if (reason.Length is 0 or > 500)
        {
            TempData["DeliveryCancelReason"] = reason.Length > 500 ? reason[..500] : reason;
            return BackToDetails(code, page, "Hãy nhập lý do hủy, tối đa 500 ký tự.");
        }

        var actorId = await GetActorIdAsync(cancellationToken);
        if (!actorId.HasValue) return Forbid();

        string? error;
        try
        {
            error = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // Chỉ AuditLog được thêm qua SaveChanges; đơn và tiền dùng ExecuteUpdate.
                // Xóa đối tượng theo dõi từ lần thử trước nếu kết nối cần thực hiện lại.
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                var now = DateTime.UtcNow;
                var orders = _context.Set<Order>()
                    .Where(order => order.Code == code && order.OrderType == "DELIVERY");

                // Cùng khóa dòng đơn như các thao tác bắt đầu giao/hoàn tất.
                var changedOrders = await orders
                    .Where(order => (order.Status == "OPEN" || order.Status == "IN_PROGRESS")
                        && order.ClosedAtUtc == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(order => order.Status, "CANCELLED")
                        .SetProperty(order => order.ClosedAtUtc, (DateTime?)now), cancellationToken);
                if (changedOrders != 1)
                    return "Chỉ được hủy đơn đang chờ tiếp nhận hoặc đang chuẩn bị. Hãy tải lại chi tiết.";

                var orderId = await orders
    .Select(order => order.Id)
    .SingleAsync(cancellationToken);

                var hasPendingRequest = await _context.Set<OrderCancellationRequest>()
                    .AnyAsync(
                        request => request.OrderId == orderId
                            && request.Status == "PENDING",
                        cancellationToken);

                if (hasPendingRequest)
                {
                    return "Khách đang có yêu cầu hủy chờ xử lý. Hãy xét duyệt yêu cầu thay vì hủy trực tiếp.";
                }

                var payments = _context.Set<Payment>()
                    .Where(payment => payment.OrderId == orderId);
                // Chặn mọi khoản đã thu, kể cả tiền cọc hoặc phương thức khác.
                if (await payments.AnyAsync(payment => payment.Status == "SUCCEEDED", cancellationToken))
                    return "Đơn đã ghi nhận thu tiền nên không thể hủy theo cách này.";

                var pending = await (
                    from payment in payments.AsNoTracking()
                    join method in _context.Set<PaymentMethod>().AsNoTracking()
                        on payment.MethodId equals method.Id
                    where payment.Status == "PENDING"
                    select new { payment.Id, payment.Amount, payment.Purpose, MethodCode = method.Code })
                    .Take(2).ToListAsync(cancellationToken);
                if (pending.Count != 1 || pending[0].MethodCode != "CASH"
                    || pending[0].Purpose != "SETTLEMENT" || pending[0].Amount <= 0m)
                    return "Đơn phải có đúng một khoản tiền mặt chờ thu hợp lệ. Hãy kiểm tra thanh toán trước khi hủy.";

                var pendingId = pending[0].Id;
                var changedPayments = await payments
                    .Where(payment => payment.Id == pendingId && payment.Status == "PENDING")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(payment => payment.Status, "CANCELLED"),
                        cancellationToken);
                if (changedPayments != 1)
                    return "Khoản chờ thu vừa thay đổi. Hãy tải lại trang trước khi hủy.";

                await _context.Set<OrderItem>()
                    .Where(item => item.OrderId == orderId && item.Status != "CANCELLED")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "CANCELLED"),
                        cancellationToken);

                _context.Set<AuditLog>().Add(new AuditLog
                {
                    ActorId = actorId.Value,
                    Action = "delivery.cancel",
                    EntityName = "Orders",
                    EntityKey = orderId.ToString(CultureInfo.InvariantCulture),
                    Summary = reason,
                    CreatedAtUtc = now
                });
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (string?)null;
            });
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            error = "Đơn đang được xử lý đồng thời. Hãy tải lại chi tiết rồi thử lại.";
        }

        if (error is not null) TempData["DeliveryCancelReason"] = reason;
        return BackToDetails(code, page, error);
    }

    private IActionResult BackToDetails(string code, int page, string? error)
    {
        TempData[error is null ? "DeliverySuccess" : "DeliveryError"] =
            error ?? $"Đã hủy đơn {code} và khoản tiền mặt chờ thu.";
        return RedirectToAction(nameof(Details),
            new { area = "Admin", code, page = Math.Max(1, page) });
    }

    private async Task<long?> GetActorIdAsync(CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id <= 0)
            return null;
        var valid = await _context.Set<DoraFood.Models.User>().AsNoTracking()
            .AnyAsync(user => user.Id == id && user.IsActive
                && user.Roles.Any(role => role.Code == "ADMIN" || role.Code == "MANAGER"),
                cancellationToken);
        return valid ? id : (long?)null;
    }

    private IQueryable<Payment> CashSettlements() =>
        from payment in _context.Set<Payment>().AsNoTracking()
        join method in _context.Set<PaymentMethod>().AsNoTracking()
            on payment.MethodId equals method.Id
        where method.Code == "CASH" && payment.Purpose == "SETTLEMENT"
        select payment;

    private static bool ValidCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 40;
}

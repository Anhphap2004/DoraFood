using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.Services;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

[Authorize]
public class CartController : Controller
{
    private const string CheckoutPurpose = "DoraFood.CashCheckout.v2";

    private readonly DoraFoodContext _context;
    private readonly CartStore _cart;
    private readonly VoucherService _voucherService;

    private long _userId;
    private Guid _stamp;

    public CartController(
        DoraFoodContext context,
        CartStore cart,
        VoucherService voucherService)
    {
        _context = context;
        _cart = cart;
        _voucherService = voucherService;
    }

    // Kiểm tra tài khoản và phiên đăng nhập trước mọi action.
    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var validClaims =
            long.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out _userId)
            && _userId > 0
            && Guid.TryParse(
                User.FindFirstValue("SecurityStamp"),
                out _stamp);

        var validAccount = validClaims
            && await _context.Set<DoraFood.Models.User>()
                .AsNoTracking()
                .AnyAsync(
                    user => user.Id == _userId
                        && user.IsActive
                        && user.SecurityStamp == _stamp,
                    HttpContext.RequestAborted);

        if (!validAccount)
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            context.Result = RedirectToAction(
                "Login",
                "Account",
                new
                {
                    returnUrl = Url.RouteUrl("DoraFoodCart")
                });

            return;
        }

        await next();
    }

    // ==================== GIỎ HÀNG ====================

    [HttpGet("/gio-hang", Name = "DoraFoodCart")]
    [HttpGet("/Cart")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Giỏ hàng mua mang về";

        var cart = await BuildCartAsync(cancellationToken);

        return View(cart);
    }

    [HttpPost("/gio-hang/them", Name = "DoraFoodAddToCart")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(
        [FromForm] CartItemInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || input.VariantId <= 0)
        {
            return Back(
                "Hãy chọn khẩu phần và nhập số lượng từ 1 đến 99.");
        }

        if (!await CanSellAsync(input.VariantId, cancellationToken))
        {
            return Back(
                "Món hoặc khẩu phần hiện không còn bán. Vui lòng chọn lại.");
        }

        var error = _cart.Add(
            _userId,
            _stamp,
            input.VariantId,
            input.Quantity);

        return Back(error, "Đã thêm món vào giỏ.");
    }

    [HttpPost("/gio-hang/cap-nhat", Name = "DoraFoodUpdateCart")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        [FromForm] CartItemInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || input.VariantId <= 0)
        {
            return Back(
                "Số lượng phải từ 1 đến 99. Muốn bỏ món, hãy chọn Xóa.");
        }

        if (!await CanSellAsync(input.VariantId, cancellationToken))
        {
            return Back(
                "Món hiện không còn bán. Anh có thể xóa món khỏi giỏ.");
        }

        var error = _cart.Update(
            _userId,
            _stamp,
            input.VariantId,
            input.Quantity);

        return Back(error, "Đã cập nhật số lượng.");
    }

    [HttpPost("/gio-hang/xoa", Name = "DoraFoodRemoveCartItem")]
    [ValidateAntiForgeryToken]
    public IActionResult Remove([FromForm] long variantId)
    {
        if (!ModelState.IsValid || variantId <= 0)
        {
            return Back("Khẩu phần không hợp lệ.");
        }

        var error = _cart.Remove(_userId, _stamp, variantId);

        return Back(error, "Đã xóa món khỏi giỏ.");
    }

    [HttpPost("/gio-hang/xoa-tat-ca", Name = "DoraFoodClearCart")]
    [ValidateAntiForgeryToken]
    public IActionResult Clear()
    {
        _cart.Clear(_userId, _stamp);

        return Back(null, "Đã xóa toàn bộ giỏ hàng.");
    }

    // ==================== THANH TOÁN ====================

    [HttpGet("/thanh-toan", Name = "DoraFoodCheckout")]
    public async Task<IActionResult> Checkout(
    CancellationToken cancellationToken = default)
    {
        var cart = await BuildCartAsync(cancellationToken);

        if (cart.Items.Count == 0 || cart.HasUnavailableItems)
        {
            return Back(
                "Giỏ hàng đang trống hoặc có món đã ngừng bán.");
        }

        // Chỉ đọc địa chỉ mặc định của tài khoản đang đăng nhập.
        var defaultAddress = await _context.Set<UserAddress>()
            .AsNoTracking()
            .Where(address =>
                address.UserId == _userId
                && address.IsDefault)
            .OrderBy(address => address.Id)
            .Select(address => new
            {
                address.RecipientName,
                address.PhoneNumber,
                address.AddressLine
            })
            .FirstOrDefaultAsync(cancellationToken);

        var input = new CheckoutInput
        {
            PaymentMethod = "CASH"
        };

        if (defaultAddress is not null)
        {
            input.ReceiverName = defaultAddress.RecipientName;
            input.PhoneNumber = defaultAddress.PhoneNumber;
            input.DeliveryAddress = defaultAddress.AddressLine;
        }

        ViewData["Title"] = "Thanh toán đơn giao hàng";

        return View(new CheckoutViewModel
        {
            Cart = cart,
            Input = input
        });
    }

    [HttpPost(
        "/thanh-toan/kiem-tra",
        Name = "DoraFoodReviewCheckout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewCheckout(
        [FromForm(Name = "Input")] CheckoutInput input,
        [FromServices] IDataProtectionProvider protectionProvider,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Thanh toán đơn giao hàng";

        var cart = await BuildCartAsync(cancellationToken);

        if (cart.Items.Count == 0 || cart.HasUnavailableItems)
        {
            return Back(
                "Giỏ hàng đã thay đổi. "
                + "Anh kiểm tra lại các món trước khi tiếp tục.");
        }

        var model = new CheckoutViewModel
        {
            Cart = cart,
            Input = input
        };

        if (!ModelState.IsValid)
        {
            return View("Checkout", model);
        }

        if (!IsValidCheckoutAmount(cart.Subtotal))
        {
            ModelState.AddModelError(
                string.Empty,
                "Tổng tiền cần là số đồng nguyên dương "
                + "và nằm trong giới hạn lưu trữ.");

            return View("Checkout", model);
        }

        input.VoucherCode = string.IsNullOrWhiteSpace(input.VoucherCode)
            ? null
            : input.VoucherCode.Trim().ToUpperInvariant();

        var voucherResult = await _voucherService.CheckAsync(
            input.VoucherCode,
            _userId,
            cart.Subtotal,
            cancellationToken);

        if (!voucherResult.IsValid)
        {
            ModelState.AddModelError(
                "Input.VoucherCode",
                voucherResult.ErrorMessage
                    ?? "Không thể áp dụng mã giảm giá.");

            return View("Checkout", model);
        }

        model.AppliedVoucherCode = voucherResult.Code;
        model.DiscountAmount = voucherResult.DiscountAmount;

        var quote = new CashCheckoutQuote(
            _userId,
            _stamp,
            cart.Version,
            DateTime.UtcNow.AddMinutes(15),
            CartFingerprint(cart),
            input.ReceiverName.Trim(),
            input.PhoneNumber.Trim(),
            input.DeliveryAddress.Trim(),
            string.IsNullOrWhiteSpace(input.Note)
                ? null
                : input.Note.Trim(),
            voucherResult.VoucherId,
            voucherResult.Code,
            voucherResult.DiscountAmount,
            voucherResult.TotalAmount);

        var protector = protectionProvider.CreateProtector(
            CheckoutPurpose);

        model.ConfirmationToken = protector.Protect(
            JsonSerializer.Serialize(quote));

        return View("Checkout", model);
    }

    [HttpPost(
        "/thanh-toan/xac-nhan-tien-mat",
        Name = "DoraFoodPlaceCashOrder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceCashOrder(
        [FromForm] string? confirmationToken,
        [FromServices] IDataProtectionProvider protectionProvider,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(confirmationToken))
        {
            return Back(
                "Vui lòng kiểm tra thông tin trước khi xác nhận đặt đơn.");
        }

        CashCheckoutQuote? quote;

        try
        {
            var protector = protectionProvider.CreateProtector(
                CheckoutPurpose);

            var json = protector.Unprotect(confirmationToken);

            quote = JsonSerializer.Deserialize<CashCheckoutQuote>(json);
        }
        catch (CryptographicException)
        {
            return Back(
                "Phiên xác nhận không hợp lệ. "
                + "Vui lòng kiểm tra lại thông tin thanh toán.");
        }
        catch (JsonException)
        {
            return Back(
                "Không đọc được thông tin xác nhận. "
                + "Vui lòng kiểm tra lại thông tin thanh toán.");
        }

        if (quote is null
            || quote.UserId != _userId
            || quote.SecurityStamp != _stamp
            || quote.CartVersion == Guid.Empty)
        {
            return Back(
                "Thông tin xác nhận không khớp với tài khoản hiện tại.");
        }

        var hasVoucherCode =
            !string.IsNullOrWhiteSpace(quote.VoucherCode);

        if (quote.VoucherId.HasValue != hasVoucherCode
            || quote.VoucherId is <= 0
            || quote.DiscountAmount < 0m
            || !IsValidCheckoutAmount(quote.TotalAmount))
        {
            return Back(
                "Thông tin giảm giá không hợp lệ. "
                + "Vui lòng kiểm tra lại trước khi đặt.");
        }

        // Cùng một phiên bản giỏ luôn tạo cùng một mã đơn.
        var orderCode = $"DF-{quote.CartVersion:N}";

        if (await CashOrderExistsAsync(orderCode, cancellationToken))
        {
            return FinishCashOrder(orderCode, quote.CartVersion);
        }

        if (quote.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Back(
                "Phiên xác nhận đã hết hạn. Vui lòng kiểm tra lại giỏ.");
        }

        string? error;

        try
        {
            error = await SaveCashOrderAsync(
                quote,
                orderCode,
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627))
        {
            // Chỉ coi là thành công nếu đơn của khách đã tồn tại.
            if (!await CashOrderExistsAsync(orderCode, cancellationToken))
            {
                throw;
            }

            error = null;
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            error =
                "Đơn đang được xử lý đồng thời. Vui lòng thử lại.";
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sql
                && sql.Number == 1205)
        {
            error =
                "Đơn đang được xử lý đồng thời. Vui lòng thử lại.";
        }

        if (error is not null
            && !await CashOrderExistsAsync(orderCode, cancellationToken))
        {
            return Back(error);
        }

        return FinishCashOrder(orderCode, quote.CartVersion);
    }

    // ==================== LƯU ĐƠN HÀNG ====================

    private async Task<string?> SaveCashOrderAsync(
        CashCheckoutQuote quote,
        string orderCode,
        CancellationToken cancellationToken)
    {
        return await _context.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();

                // Kiểm tra khi giao dịch được chạy lại.
                if (await CashOrderExistsAsync(
                    orderCode,
                    cancellationToken))
                {
                    return (string?)null;
                }

                await using var transaction =
                    await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);

                // Giữ khóa voucher đến khi giao dịch hoàn tất.
                if (quote.VoucherId.HasValue)
                {
                    var voucherId = quote.VoucherId.Value;

                    var lockedVoucher = await _context.Set<Voucher>()
                        .FromSqlInterpolated($@"
                            SELECT *
                            FROM dbo.Vouchers WITH (UPDLOCK, HOLDLOCK)
                            WHERE Id = {voucherId}")
                        .AsNoTracking()
                        .SingleOrDefaultAsync(cancellationToken);

                    if (lockedVoucher is null)
                    {
                        return "Mã giảm giá không còn tồn tại. "
                            + "Vui lòng kiểm tra lại thông tin.";
                    }
                }

                // Request khác có thể đã tạo đơn trong lúc chờ khóa.
                if (await CashOrderExistsAsync(
                    orderCode,
                    cancellationToken))
                {
                    return (string?)null;
                }

                if (quote.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    return "Phiên xác nhận đã hết hạn. "
                        + "Vui lòng kiểm tra lại thông tin.";
                }

                var cart = await BuildCartAsync(cancellationToken);

                if (cart.Version != quote.CartVersion
                    || cart.Items.Count == 0
                    || cart.HasUnavailableItems
                    || !IsValidCheckoutAmount(cart.Subtotal)
                    || CartFingerprint(cart) != quote.Fingerprint)
                {
                    return "Giỏ hoặc giá món đã thay đổi. "
                        + "Vui lòng kiểm tra lại trước khi đặt.";
                }

                // Đọc lại điều kiện và số lượt trong giao dịch.
                var voucherResult = await _voucherService.CheckAsync(
                    quote.VoucherCode,
                    _userId,
                    cart.Subtotal,
                    cancellationToken);

                if (!voucherResult.IsValid)
                {
                    return voucherResult.ErrorMessage
                        ?? "Không thể áp dụng mã giảm giá.";
                }

                // Không tự đổi số tiền khách đã kiểm tra.
                if (voucherResult.VoucherId != quote.VoucherId
                    || voucherResult.DiscountAmount != quote.DiscountAmount
                    || voucherResult.TotalAmount != quote.TotalAmount)
                {
                    return "Ưu đãi đã thay đổi. "
                        + "Vui lòng kiểm tra lại số tiền trước khi đặt.";
                }

                var methodId = await _context.Set<PaymentMethod>()
                    .AsNoTracking()
                    .Where(method =>
                        method.Code == "CASH"
                        && method.IsActive)
                    .Select(method => (long?)method.Id)
                    .SingleOrDefaultAsync(cancellationToken);

                if (!methodId.HasValue)
                {
                    return "Phương thức tiền mặt hiện chưa được bật.";
                }

                var order = BuildCashOrder(
                    quote,
                    cart,
                    voucherResult,
                    methodId.Value);

                _context.Set<Order>().Add(order);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return (string?)null;
            });
    }

    private Order BuildCashOrder(
        CashCheckoutQuote quote,
        CartViewModel cart,
        VoucherCheckResult voucherResult,
        long methodId)
    {
        var now = DateTime.UtcNow;

        var order = new Order
        {
            Code = $"DF-{quote.CartVersion:N}",
            CustomerId = _userId,
            CreatedBy = _userId,
            OrderType = "DELIVERY",
            Status = "OPEN",
            GuestName = quote.ReceiverName,
            GuestPhone = quote.PhoneNumber,
            DeliveryAddress = quote.DeliveryAddress,
            Note = quote.Note,
            OpenedAtUtc = now
        };

        foreach (var item in cart.Items)
        {
            var name = $"{item.DishName} · {item.VariantName}";

            order.OrderItems.Add(new OrderItem
            {
                VariantId = item.VariantId,
                NameSnapshot = name.Length <= 250 ? name : name[..250],
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice.GetValueOrDefault(),
                Status = "NEW",
                CreatedAtUtc = now
            });
        }

        if (voucherResult.VoucherId.HasValue)
        {
            order.OrderDiscounts.Add(new OrderDiscount
            {
                VoucherId = voucherResult.VoucherId.Value,
                Description = $"Áp dụng mã {voucherResult.Code}",
                Amount = voucherResult.DiscountAmount,
                ApprovedBy = null
            });
        }

        order.Payments.Add(new Payment
        {
            MethodId = methodId,
            Purpose = "SETTLEMENT",
            Amount = voucherResult.TotalAmount,
            Status = "PENDING",
            IdempotencyKey = quote.CartVersion,
            CreatedAtUtc = now
        });

        return order;
    }

    private Task<bool> CashOrderExistsAsync(
        string orderCode,
        CancellationToken cancellationToken)
    {
        return _context.Set<Order>()
            .AsNoTracking()
            .AnyAsync(
                order => order.Code == orderCode
                    && order.CustomerId == _userId
                    && order.OrderType == "DELIVERY",
                cancellationToken);
    }

    private IActionResult FinishCashOrder(
        string orderCode,
        Guid cartVersion)
    {
        // Không xóa giỏ mới nếu khách đã thay đổi giỏ ở request khác.
        _cart.ClearIfVersion(_userId, _stamp, cartVersion);

        TempData["CartSuccess"] =
            $"Đặt đơn {orderCode} thành công. "
            + "Anh thanh toán tiền mặt khi nhận hàng.";

        return RedirectToRoute("DoraFoodCart");
    }

    // ==================== ĐỌC GIỎ VÀ GIÁ MÓN ====================

    private async Task<CartViewModel> BuildCartAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = _cart.ReadSnapshot(_userId, _stamp);
        var quantities = snapshot.Items;

        var model = new CartViewModel
        {
            Version = snapshot.Version
        };

        if (quantities.Count == 0)
        {
            return model;
        }

        var variantIds = quantities.Keys.ToArray();

        var categories = await VisibleCategoryIdsAsync(
            cancellationToken);

        var variants = await _context.Set<DishVariant>()
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.DishId,
                variant.Name,
                variant.SellingUnit,
                variant.Price,
                variant.IsAvailable,
                DishName = variant.Dish.Name,
                CategoryId = variant.Dish.CategoryId,
                DishActive = variant.Dish.IsActive
            })
            .ToDictionaryAsync(
                variant => variant.Id,
                cancellationToken);

        var dishIds = variants.Values
            .Select(variant => variant.DishId)
            .Distinct()
            .ToArray();

        var images = await _context.Set<DishImage>()
            .AsNoTracking()
            .Where(image => dishIds.Contains(image.DishId))
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => new
            {
                image.DishId,
                image.MediaFile.Url,
                image.MediaFile.AltText
            })
            .ToListAsync(cancellationToken);

        var primaryImages = images
            .GroupBy(image => image.DishId)
            .ToDictionary(
                group => group.Key,
                group => group.First());

        foreach (var entry in quantities)
        {
            var line = new CartLineViewModel
            {
                VariantId = entry.Key,
                Quantity = entry.Value,
                DishName = "Món không còn phục vụ"
            };

            if (variants.TryGetValue(entry.Key, out var variant))
            {
                line.DishId = variant.DishId;
                line.DishName = variant.DishName;
                line.VariantName = variant.Name;
                line.SellingUnit = variant.SellingUnit;

                line.IsAvailable =
                    variant.IsAvailable
                    && variant.DishActive
                    && categories.Contains(variant.CategoryId);

                line.UnitPrice = line.IsAvailable
                    ? variant.Price
                    : null;

                if (primaryImages.TryGetValue(
                    variant.DishId,
                    out var image))
                {
                    line.ImageUrl = ImageUrl(image.Url);
                    line.ImageAlt = image.AltText;
                }
            }

            model.Items.Add(line);
        }

        return model;
    }

    private async Task<bool> CanSellAsync(
        long variantId,
        CancellationToken cancellationToken)
    {
        var categoryId = await _context.Set<DishVariant>()
            .AsNoTracking()
            .Where(variant =>
                variant.Id == variantId
                && variant.IsAvailable
                && variant.Dish.IsActive)
            .Select(variant => (long?)variant.Dish.CategoryId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!categoryId.HasValue)
        {
            return false;
        }

        var visibleCategories = await VisibleCategoryIdsAsync(
            cancellationToken);

        return visibleCategories.Contains(categoryId.Value);
    }

    // Loại danh mục ẩn, thiếu cha hoặc có vòng lặp cha/con.
    private async Task<HashSet<long>> VisibleCategoryIdsAsync(
        CancellationToken cancellationToken)
    {
        var categories = await _context.Set<DishCategory>()
            .AsNoTracking()
            .Where(category => category.IsActive)
            .Select(category => new
            {
                category.Id,
                category.ParentId
            })
            .ToDictionaryAsync(
                category => category.Id,
                cancellationToken);

        return categories.Keys.Where(id =>
        {
            var visited = new HashSet<long>();
            long? current = id;

            while (current.HasValue)
            {
                if (!visited.Add(current.Value)
                    || !categories.TryGetValue(
                        current.Value,
                        out var category))
                {
                    return false;
                }

                current = category.ParentId;
            }

            return true;
        }).ToHashSet();
    }

    // ==================== HÀM HỖ TRỢ ====================

    private IActionResult Back(
        string? error,
        string success = "")
    {
        TempData[error is null ? "CartSuccess" : "CartError"] =
            error ?? success;

        return RedirectToRoute("DoraFoodCart");
    }

    private static bool IsValidCheckoutAmount(decimal amount)
    {
        return amount > 0m
            && amount <= 99999999999999999.99m
            && amount == decimal.Truncate(amount);
    }

    private static string CartFingerprint(CartViewModel cart)
    {
        var lines = cart.Items
            .OrderBy(item => item.VariantId)
            .Select(item => new
            {
                item.VariantId,
                item.Quantity,
                item.UnitPrice,
                item.DishName,
                item.VariantName
            });

        var bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(lines));

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string ImageUrl(string? value)
    {
        const string fallback = "~/assets/img/dish-placeholder.svg";

        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        value = value.Trim();

        if (value.Contains('\\'))
        {
            return fallback;
        }

        if (value.StartsWith("~/", StringComparison.Ordinal))
        {
            return value;
        }

        if (value.StartsWith("/", StringComparison.Ordinal)
            && !value.StartsWith("//", StringComparison.Ordinal))
        {
            return value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps
                || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : fallback;
    }

    private sealed record CashCheckoutQuote(
        long UserId,
        Guid SecurityStamp,
        Guid CartVersion,
        DateTime ExpiresAtUtc,
        string Fingerprint,
        string ReceiverName,
        string PhoneNumber,
        string DeliveryAddress,
        string? Note,
        long? VoucherId,
        string? VoucherCode,
        decimal DiscountAmount,
        decimal TotalAmount);
}
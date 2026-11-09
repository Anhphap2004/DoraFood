using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.Services;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
namespace DoraFood.Controllers;

[Authorize]
public class CartController : Controller
{
    private readonly DoraFoodContext _context;
    private readonly CartStore _cart;
    private long _userId;
    private Guid _stamp;

    public CartController(DoraFoodContext context, CartStore cart)
    {
        _context = context;
        _cart = cart;
    }

    // Mọi action đều xác minh tài khoản đang hoạt động và phiên đăng nhập còn hợp lệ.
    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var validClaims = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out _userId)
            && _userId > 0 && Guid.TryParse(User.FindFirstValue("SecurityStamp"), out _stamp);
        if (!validClaims || !await _context.Set<DoraFood.Models.User>().AsNoTracking()
            .AnyAsync(u => u.Id == _userId && u.IsActive && u.SecurityStamp == _stamp,
                HttpContext.RequestAborted))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result = RedirectToAction("Login", "Account",
                new { returnUrl = Url.RouteUrl("DoraFoodCart") });
            return;
        }
        await next();
    }

    [HttpGet("/gio-hang", Name = "DoraFoodCart")]
    [HttpGet("/Cart")]
    public async Task<IActionResult> Index(
     CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Giỏ hàng mua mang về";

        var cart = await BuildCartAsync(cancellationToken);

        return View(cart);
    }

    [HttpGet("/thanh-toan", Name = "DoraFoodCheckout")]
    public async Task<IActionResult> Checkout(
    CancellationToken cancellationToken = default)
    {
        var cart = await BuildCartAsync(cancellationToken);

        if (cart.Items.Count == 0 || cart.HasUnavailableItems)
        {
            TempData["CartError"] =
                "Giỏ hàng đang trống hoặc có món đã ngừng bán.";

            return RedirectToRoute("DoraFoodCart");
        }

        ViewData["Title"] = "Thanh toán đơn giao hàng";

        var model = new CheckoutViewModel
        {
            Cart = cart
        };

        return View(model);
    }

    [HttpPost("/thanh-toan/kiem-tra", Name = "DoraFoodReviewCheckout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewCheckout(
     [FromForm(Name = "Input")] CheckoutInput input,
     [FromServices] IDataProtectionProvider protectionProvider,
     CancellationToken cancellationToken = default)
    {
        var cart = await BuildCartAsync(cancellationToken);

        if (cart.Items.Count == 0 || cart.HasUnavailableItems)
        {
            TempData["CartError"] =
                "Giỏ hàng đã thay đổi. Anh kiểm tra lại các món trước khi tiếp tục.";

            return RedirectToRoute("DoraFoodCart");
        }

        var model = new CheckoutViewModel
        {
            Cart = cart,
            Input = input
        };

        ViewData["Title"] = "Thanh toán đơn giao hàng";

        if (!ModelState.IsValid)
        {
            return View("Checkout", model);
        }

       

        if (!IsValidCheckoutAmount(cart.Subtotal))
        {
            ModelState.AddModelError(
                string.Empty,
                "Tổng tiền cần là số đồng nguyên dương và nằm trong giới hạn lưu trữ.");

            return View("Checkout", model);
        }

        var quote = new CashCheckoutQuote(
            _userId,
            _stamp,
            cart.Version,
            DateTime.UtcNow.AddMinutes(15),
            CartFingerprint(cart),
            input.ReceiverName.Trim(),
            input.PhoneNumber.Trim(),
            input.DeliveryAddress.Trim(),
            string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim());

        var protector = protectionProvider.CreateProtector("DoraFood.CashCheckout.v1");
        model.ConfirmationToken = protector.Protect(JsonSerializer.Serialize(quote));

        return View("Checkout", model);
    }
    [HttpPost("/thanh-toan/xac-nhan-tien-mat", Name = "DoraFoodPlaceCashOrder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceCashOrder(
[FromForm] string? confirmationToken,
[FromServices] IDataProtectionProvider protectionProvider,
CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(confirmationToken))
        {
            return Back("Vui lòng kiểm tra thông tin trước khi xác nhận đặt đơn.");
        }

        CashCheckoutQuote? quote;

        try
        {
            var protector = protectionProvider.CreateProtector("DoraFood.CashCheckout.v1");
            var json = protector.Unprotect(confirmationToken);
            quote = JsonSerializer.Deserialize<CashCheckoutQuote>(json);
        }
        catch (CryptographicException)
        {
            return Back("Mã xác nhận không hợp lệ. Vui lòng kiểm tra lại giỏ.");
        }
        catch (JsonException)
        {
            return Back("Không đọc được thông tin xác nhận. Vui lòng thử lại.");
        }

        if (quote == null
            || quote.UserId != _userId
            || quote.SecurityStamp != _stamp
            || quote.CartVersion == Guid.Empty)
        {
            return Back("Thông tin xác nhận không khớp với tài khoản hiện tại.");
        }

        // Một phiên bản giỏ luôn cho ra cùng một mã đơn.
        var orderCode = $"DF-{quote.CartVersion:N}";

        // Kiểm tra trước khi đọc lại giỏ để lần gửi lại vẫn nhận được
        // kết quả của đơn đã lưu, kể cả khi giỏ đã được dọn.
        if (await CashOrderExistsAsync(orderCode, cancellationToken))
        {
            return FinishCashOrder(orderCode, quote.CartVersion);
        }

        if (quote.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Back("Phiên xác nhận đã hết hạn. Vui lòng kiểm tra lại giỏ.");
        }

        var cart = await BuildCartAsync(cancellationToken);

        if (cart.Version != quote.CartVersion
            || cart.Items.Count == 0
            || cart.HasUnavailableItems
            || !IsValidCheckoutAmount(cart.Subtotal)
            || CartFingerprint(cart) != quote.Fingerprint)
        {
            // Một request khác có thể vừa lưu đơn và dọn giỏ.
            if (await CashOrderExistsAsync(orderCode, cancellationToken))
            {
                return FinishCashOrder(orderCode, quote.CartVersion);
            }

            return Back("Giỏ hoặc giá món đã thay đổi. Vui lòng kiểm tra lại trước khi đặt.");
        }

        var methodId = await _context.Set<PaymentMethod>()
            .AsNoTracking()
            .Where(method => method.Code == "CASH" && method.IsActive)
            .Select(method => (long?)method.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (!methodId.HasValue)
        {
            return Back("Phương thức tiền mặt hiện chưa được bật.");
        }

        var now = DateTime.UtcNow;

        var order = new Order
        {
            Code = orderCode,
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

        order.Payments.Add(new Payment
        {
            MethodId = methodId.Value,
            Purpose = "SETTLEMENT",
            Amount = cart.Subtotal,
            Status = "PENDING",
            IdempotencyKey = quote.CartVersion,
            CreatedAtUtc = now
        });

        _context.Set<Order>().Add(order);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // SQL có ràng buộc duy nhất trên mã đơn và IdempotencyKey.
            // Nếu hai request cùng ghi, chỉ một request tạo được đơn.
            if (!await CashOrderExistsAsync(orderCode, cancellationToken))
            {
                throw;
            }
        }

        return FinishCashOrder(orderCode, quote.CartVersion);
    }

    private Task<bool> CashOrderExistsAsync(
        string orderCode, CancellationToken cancellationToken)
    {
        return _context.Set<Order>().AsNoTracking().AnyAsync(
            order => order.Code == orderCode
                && order.CustomerId == _userId
                && order.OrderType == "DELIVERY",
            cancellationToken);
    }

    private IActionResult FinishCashOrder(string orderCode, Guid cartVersion)
    {
        _cart.ClearIfVersion(_userId, _stamp, cartVersion);

        TempData["CartSuccess"] =
            $"Đặt đơn {orderCode} thành công. Anh thanh toán tiền mặt khi nhận hàng.";

        return RedirectToRoute("DoraFoodCart");
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
        string? Note);

    private static string CartFingerprint(CartViewModel cart)
    {
        var lines = cart.Items.OrderBy(item => item.VariantId).Select(item => new
        {
            item.VariantId,
            item.Quantity,
            item.UnitPrice,
            item.DishName,
            item.VariantName
        });

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(lines));

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static bool IsValidCheckoutAmount(decimal amount)
    {
        return amount > 0
            && amount <= 99999999999999999.99m
            && amount == decimal.Truncate(amount);
    }

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

        // Đọc lại thông tin và giá hiện tại của từng khẩu phần từ SQL Server.
        var variantIds = quantities.Keys.ToArray();
        var categories = await VisibleCategoryIdsAsync(cancellationToken);

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
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        // Lấy ảnh cho các món có trong giỏ.
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
            .ToDictionary(group => group.Key, group => group.First());

        // Ghép số lượng trong giỏ với thông tin món vừa đọc từ SQL.
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

                line.UnitPrice = line.IsAvailable ? variant.Price : null;

                if (primaryImages.TryGetValue(variant.DishId, out var image))
                {
                    line.ImageUrl = ImageUrl(image.Url);
                    line.ImageAlt = image.AltText;
                }
            }

            model.Items.Add(line);
        }

        return model;
    }

    [HttpPost("/gio-hang/them", Name = "DoraFoodAddToCart")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([FromForm] CartItemInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || input.VariantId <= 0)
            return Back("Hãy chọn khẩu phần và nhập số lượng từ 1 đến 99.");
        if (!await CanSellAsync(input.VariantId, cancellationToken))
            return Back("Món hoặc khẩu phần hiện không còn bán. Vui lòng chọn lại.");
        return Back(_cart.Add(_userId, _stamp, input.VariantId, input.Quantity), "Đã thêm món vào giỏ.");
    }

    [HttpPost("/gio-hang/cap-nhat", Name = "DoraFoodUpdateCart")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromForm] CartItemInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || input.VariantId <= 0)
            return Back("Số lượng phải từ 1 đến 99. Muốn bỏ món, hãy chọn Xóa.");
        if (!await CanSellAsync(input.VariantId, cancellationToken))
            return Back("Món hiện không còn bán. Anh có thể xóa món khỏi giỏ.");
        return Back(_cart.Update(_userId, _stamp, input.VariantId, input.Quantity), "Đã cập nhật số lượng.");
    }

    [HttpPost("/gio-hang/xoa", Name = "DoraFoodRemoveCartItem")]
    [ValidateAntiForgeryToken]
    public IActionResult Remove([FromForm] long variantId)
    {
        if (!ModelState.IsValid || variantId <= 0) return Back("Khẩu phần không hợp lệ.");
        return Back(_cart.Remove(_userId, _stamp, variantId), "Đã xóa món khỏi giỏ.");
    }

    [HttpPost("/gio-hang/xoa-tat-ca", Name = "DoraFoodClearCart")]
    [ValidateAntiForgeryToken]
    public IActionResult Clear()
    {
        _cart.Clear(_userId, _stamp);
        return Back(null, "Đã xóa toàn bộ giỏ hàng.");
    }

    private IActionResult Back(string? error, string success = "")
    {
        TempData[error == null ? "CartSuccess" : "CartError"] = error ?? success;
        return RedirectToRoute("DoraFoodCart");
    }

    private async Task<bool> CanSellAsync(long variantId, CancellationToken cancellationToken)
    {
        var categoryId = await _context.Set<DishVariant>().AsNoTracking()
            .Where(v => v.Id == variantId && v.IsAvailable && v.Dish.IsActive)
            .Select(v => (long?)v.Dish.CategoryId).SingleOrDefaultAsync(cancellationToken);
        return categoryId.HasValue
            && (await VisibleCategoryIdsAsync(cancellationToken)).Contains(categoryId.Value);
    }

    // Khớp quy tắc thực đơn: loại danh mục ẩn, thiếu cha hoặc vòng lặp quan hệ cha/con.
    private async Task<HashSet<long>> VisibleCategoryIdsAsync(CancellationToken cancellationToken)
    {
        var categories = await _context.Set<DishCategory>().AsNoTracking().Where(c => c.IsActive)
            .Select(c => new { c.Id, c.ParentId }).ToDictionaryAsync(c => c.Id, cancellationToken);
        return categories.Keys.Where(id =>
        {
            var visited = new HashSet<long>();
            long? current = id;
            while (current.HasValue)
            {
                if (!visited.Add(current.Value) || !categories.TryGetValue(current.Value, out var category))
                    return false;
                current = category.ParentId;
            }
            return true;
        }).ToHashSet();
    }

    private static string ImageUrl(string? value)
    {
        const string fallback = "~/assets/img/dish-placeholder.svg";
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();
        if (value.Contains('\\')) return fallback;
        if (value.StartsWith("~/", StringComparison.Ordinal)) return value;
        if (value.StartsWith("/", StringComparison.Ordinal) && !value.StartsWith("//", StringComparison.Ordinal))
            return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri : fallback;
    }
}

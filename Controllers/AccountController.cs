using System.ComponentModel.DataAnnotations;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using AppUser = DoraFood.Models.User;

namespace DoraFood.Controllers
{
    public class AccountController : Controller
    {
        private readonly DoraFoodContext _context;
        private readonly IPasswordHasher<AppUser> _passwordHasher;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            DoraFoodContext context,
            IPasswordHasher<AppUser> passwordHasher,
            ILogger<AccountController> logger)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        // Dữ liệu được phép nhận từ form đăng ký.
        public class RegisterInput
        {
            [Display(Name = "Tên đăng nhập")]
            [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
            [StringLength(100, MinimumLength = 3,
                ErrorMessage = "Tên đăng nhập từ 3 đến 100 ký tự.")]
            [RegularExpression(@"[a-zA-Z0-9._-]+",
                ErrorMessage =
                    "Tên đăng nhập chỉ gồm chữ không dấu, số, dấu chấm, gạch dưới hoặc gạch ngang.")]
            public string UserName { get; set; } = string.Empty;

            [Display(Name = "Email")]
            [Required(ErrorMessage = "Vui lòng nhập email.")]
            [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
            [StringLength(254,
                ErrorMessage = "Email tối đa 254 ký tự.")]
            public string Email { get; set; } = string.Empty;

            [Display(Name = "Mật khẩu")]
            [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
            [StringLength(128, MinimumLength = 8,
                ErrorMessage = "Mật khẩu từ 8 đến 128 ký tự.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Nhập lại mật khẩu")]
            [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu.")]
            [DataType(DataType.Password)]
            [Compare(nameof(Password),
                ErrorMessage = "Mật khẩu nhập lại không khớp.")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }
        public class LoginInput
        {
            [Display(Name = "Tên đăng nhập")]
            [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
            [StringLength(100,
                ErrorMessage = "Tên đăng nhập tối đa 100 ký tự.")]
            public string UserName { get; set; } = string.Empty;

            [Display(Name = "Mật khẩu")]
            [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
            [StringLength(128,
                ErrorMessage = "Mật khẩu tối đa 128 ký tự.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Ghi nhớ đăng nhập")]
            public bool RememberMe { get; set; }

            [StringLength(2048)]
            public string? ReturnUrl { get; set; }
        }

        // GET: /Account/Register
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View(new RegisterInput());
        }

        // POST: /Account/Register
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterInput input)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            if (!ModelState.IsValid)
            {
                return View(input);
            }

            var cancellationToken = HttpContext.RequestAborted;

            var userName = input.UserName.Trim();
            var email = input.Email.Trim();

            var normalizedUserName = userName.ToUpperInvariant();
            var normalizedEmail = email.ToUpperInvariant();

            // Kiểm tra tên đăng nhập trùng.
            var userNameExists = await _context.Set<AppUser>()
                .AnyAsync(
                    u => u.NormalizedUserName == normalizedUserName,
                    cancellationToken);

            if (userNameExists)
            {
                ModelState.AddModelError(
                    nameof(RegisterInput.UserName),
                    "Tên đăng nhập đã được sử dụng.");
            }

            // Kiểm tra email trùng.
            var emailExists = await _context.Set<AppUser>()
                .AnyAsync(
                    u => u.NormalizedEmail == normalizedEmail,
                    cancellationToken);

            if (emailExists)
            {
                ModelState.AddModelError(
                    nameof(RegisterInput.Email),
                    "Email đã được sử dụng.");
            }

            if (!ModelState.IsValid)
            {
                return View(input);
            }

            // Server tự chọn vai trò khách hàng.
            var customerRole = await _context.Set<Role>()
                .FirstOrDefaultAsync(
                    r => r.Code == "CUSTOMER",
                    cancellationToken);

            if (customerRole == null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Chưa cấu hình vai trò khách hàng. Vui lòng liên hệ quản trị.");

                return View(input);
            }

            var user = new AppUser
            {
                UserName = userName,
                NormalizedUserName = normalizedUserName,

                Email = email,
                NormalizedEmail = normalizedEmail,

                SecurityStamp = Guid.NewGuid(),
                EmailConfirmed = false,
                IsActive = true,

                AccessFailedCount = 0,
                LockoutEndUtc = null,

                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = null
            };

            // Băm mật khẩu; không lưu mật khẩu gốc.
            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                input.Password);

            // Gán vai trò thông qua quan hệ Users–Roles.
            user.Roles.Add(customerRole);

            _context.Set<AppUser>().Add(user);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is SqlException sqlException
                      && (sqlException.Number == 2601
                          || sqlException.Number == 2627))
            {
                // Xử lý trường hợp yêu cầu khác vừa đăng ký trùng.
                ModelState.AddModelError(
                    string.Empty,
                    "Tên đăng nhập hoặc email đã được sử dụng. Vui lòng chọn thông tin khác.");

                return View(input);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu tài khoản đăng ký.");

                ModelState.AddModelError(
                    string.Empty,
                    "Chưa thể tạo tài khoản lúc này. Vui lòng thử lại sau.");

                return View(input);
            }

            TempData["AuthSuccess"] =
                "Đăng ký thành công! Bạn có thể đăng nhập.";

            return RedirectToAction("Login");
        }

        // GET: /Account/Login
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectAfterLogin(returnUrl);
            }

            return View(new LoginInput
            {
                ReturnUrl = Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : null
            });
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginInput input)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectAfterLogin(input.ReturnUrl);
            }

            if (!ModelState.IsValid)
            {
                return View(input);
            }

            const string loginError =
                "Thông tin đăng nhập không hợp lệ hoặc tài khoản tạm thời chưa thể đăng nhập.";

            var cancellationToken = HttpContext.RequestAborted;

            var normalizedUserName = input.UserName
                .Trim()
                .ToUpperInvariant();

            // Đọc tài khoản cùng các vai trò đang được gán.
            var user = await _context.Set<AppUser>()
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(
                    u => u.NormalizedUserName == normalizedUserName,
                    cancellationToken);

            var now = DateTime.UtcNow;

            // Không tồn tại, ngừng hoạt động hoặc đang bị khóa.
            if (user == null
                || !user.IsActive
                || (user.LockoutEndUtc.HasValue
                    && user.LockoutEndUtc.Value > now))
            {
                ModelState.AddModelError(string.Empty, loginError);
                return View(input);
            }

            // Hết thời gian khóa: bắt đầu lại bộ đếm.
            if (user.LockoutEndUtc.HasValue)
            {
                user.LockoutEndUtc = null;
                user.AccessFailedCount = 0;
            }

            // Kiểm tra mật khẩu bằng bản băm đã lưu.
            // Không Trim() mật khẩu.
            var verificationResult =
                _passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    input.Password);

            if (verificationResult == PasswordVerificationResult.Failed)
            {
                user.AccessFailedCount++;

                // Sai 5 lần thì khóa đăng nhập trong 15 phút.
                if (user.AccessFailedCount >= 5)
                {
                    user.LockoutEndUtc = now.AddMinutes(15);
                    user.AccessFailedCount = 0;
                }

                user.UpdatedAtUtc = now;

                if (!await SaveLoginStateAsync())
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Chưa thể xử lý đăng nhập. Vui lòng thử lại sau.");

                    return View(input);
                }

                ModelState.AddModelError(string.Empty, loginError);
                return View(input);
            }

            // Nâng cấp bản băm nếu cấu hình băm hiện tại yêu cầu.
            if (verificationResult
                == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(
                    user,
                    input.Password);
            }

            // Đăng nhập đúng: xóa bộ đếm sai.
            user.AccessFailedCount = 0;
            user.LockoutEndUtc = null;
            user.UpdatedAtUtc = now;

            // Chỉ cấp cookie sau khi lưu trạng thái thành công.
            if (!await SaveLoginStateAsync())
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Chưa thể xử lý đăng nhập. Vui lòng thử lại sau.");

                return View(input);
            }

            // Các thông tin nhận diện được lưu trong vé xác thực.
            var claims = new List<Claim>
    {
        new Claim(
            ClaimTypes.NameIdentifier,
            user.Id.ToString()),

        new Claim(
            ClaimTypes.Name,
            user.UserName),

        new Claim(
            "SecurityStamp",
            user.SecurityStamp.ToString())
    };

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                claims.Add(new Claim(
                    ClaimTypes.Email,
                    user.Email));
            }

            foreach (var role in user.Roles)
            {
                claims.Add(new Claim(
                    ClaimTypes.Role,
                    role.Code));
            }

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            var properties = new AuthenticationProperties
            {
                IsPersistent = input.RememberMe,
                AllowRefresh = true
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                properties);

            return RedirectAfterLogin(input.ReturnUrl);
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/AccessDenied
        [HttpGet]
        public IActionResult AccessDenied()
        {
            Response.StatusCode = 403;
            return View();
        }

        // Chỉ chuyển hướng về đường dẫn bên trong website.
        private IActionResult RedirectAfterLogin(string? returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl!);
            }

            return RedirectToAction("Index", "Home");
        }

        // Lưu trạng thái đăng nhập và xử lý lỗi database.
        private async Task<bool> SaveLoginStateAsync()
        {
            try
            {
                await _context.SaveChangesAsync(
                    HttpContext.RequestAborted);

                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Tài khoản đã được cập nhật đồng thời khi đăng nhập.");

                return false;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu trạng thái đăng nhập.");

                return false;
            }
        }
    }
}
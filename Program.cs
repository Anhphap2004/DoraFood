using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký MVC.
builder.Services.AddControllersWithViews();

// Đọc chuỗi kết nối SQL Server.
var connectionString = builder.Configuration
    .GetConnectionString("DoraFoodConnection")
    ?? throw new InvalidOperationException(
        "Không tìm thấy ConnectionStrings:DoraFoodConnection.");

// Đăng ký DbContext.
builder.Services.AddDbContext<DoraFoodContext>(options =>
    options.UseSqlServer(connectionString));

// Đăng ký dịch vụ băm và kiểm tra mật khẩu.
builder.Services.AddScoped<
    IPasswordHasher<User>,
    PasswordHasher<User>>();

// Đăng ký xác thực bằng cookie.
builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Trang được mở khi cần đăng nhập.
        options.LoginPath = "/Account/Login";

        // Trang thông báo khi không đủ quyền.
        options.AccessDeniedPath = "/Account/AccessDenied";

        // Cấu hình cookie.
        options.Cookie.Name = "DoraFood.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        // Thời hạn vé xác thực.
        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        // Cho phép gia hạn khi người dùng tiếp tục hoạt động.
        options.SlidingExpiration = true;
    });

// Đăng ký dịch vụ phân quyền.
builder.Services.AddAuthorization();

var app = builder.Build();

// Xử lý lỗi ngoài môi trường Development.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Đọc cookie và xác định người dùng hiện tại.
app.UseAuthentication();

// Kiểm tra quyền truy cập.
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
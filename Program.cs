using DoraFood.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký MVC.
builder.Services.AddControllersWithViews();

// Đọc chuỗi kết nối từ appsettings.json.
var connectionString = builder.Configuration
    .GetConnectionString("DoraFoodConnection")
    ?? throw new InvalidOperationException(
        "Không tìm thấy ConnectionStrings:DoraFoodConnection.");

// Đăng ký DbContext sử dụng SQL Server.
builder.Services.AddDbContext<DoraFoodContext>(options =>
    options.UseSqlServer(connectionString));

var app = builder.Build();

// Cấu hình xử lý lỗi khi triển khai.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
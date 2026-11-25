using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.Filters;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using AccountUser = DoraFood.Models.User;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN")]
[RequirePermission("users.manage")]
[AutoValidateAntiforgeryToken]
[Route("admin/nhan-vien")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffAccountsController : Controller
{
    private readonly DoraFoodContext _db;
    private readonly IPasswordHasher<AccountUser> _passwordHasher;

    public StaffAccountsController(
        DoraFoodContext db, IPasswordHasher<AccountUser> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [HttpGet("tao", Name = "DoraFoodCreateStaff")]
    public IActionResult Create() => View(new StaffAccountInput());

    [HttpPost("tao")]
    public async Task<IActionResult> Create(
        StaffAccountInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var userName = input.UserName.Trim();
        var email = input.Email.Trim();
        var normalizedUserName = userName.ToUpperInvariant();
        var normalizedEmail = email.ToUpperInvariant();

        if (await _db.Set<AccountUser>().AnyAsync(
            user => user.NormalizedUserName == normalizedUserName
                || user.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc email đã được sử dụng.");
            return View(input);
        }

        var role = await _db.Set<Role>()
            .SingleOrDefaultAsync(item => item.Code == "WAITER", cancellationToken);

        if (role is null)
        {
            ModelState.AddModelError(string.Empty, "Cơ sở dữ liệu chưa có vai trò WAITER.");
            return View(input);
        }

        var account = new AccountUser
        {
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            IsActive = true,
            SecurityStamp = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };

        account.PasswordHash = _passwordHasher.HashPassword(account, input.Password);
        account.Roles.Add(role);
        _db.Set<AccountUser>().Add(account);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc email vừa được sử dụng.");
            return View(input);
        }

        TempData["StaffSuccess"] = $"Đã tạo tài khoản phục vụ {userName}.";
        return RedirectToAction(nameof(Create));
    }

    public sealed class StaffAccountInput
    {
        [Display(Name = "Tên đăng nhập")]
        [Required(ErrorMessage = "Nhập tên đăng nhập.")]
        [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9_.-]{2,99}$",
            ErrorMessage = "Dùng 3–100 ký tự chữ không dấu, số, dấu chấm, gạch dưới hoặc gạch ngang.")]
        public string UserName { get; set; } = string.Empty;

        [Display(Name = "Email")]
        [Required(ErrorMessage = "Nhập email.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Mật khẩu")]
        [DataType(DataType.Password)]
        [Required(ErrorMessage = "Nhập mật khẩu.")]
        [StringLength(128, MinimumLength = 12, ErrorMessage = "Mật khẩu cần từ 12 đến 128 ký tự.")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Nhập lại mật khẩu")]
        [DataType(DataType.Password)]
        [Required(ErrorMessage = "Nhập lại mật khẩu.")]
        [Compare(nameof(Password), ErrorMessage = "Hai mật khẩu không trùng nhau.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}

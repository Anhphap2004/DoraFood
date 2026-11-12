using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using AccountUser = DoraFood.Models.User;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN")]
[Route("admin/accounts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminAccountsController : AdminModuleController
{
    protected override string[] AllowedRoles => new[] { "ADMIN" };

    public AdminAccountsController(DoraFoodContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken,
        string? keyword = null, string state = "all", long? roleId = null, int pageNumber = 1)
    {
        if (!ModelState.IsValid || keyword?.Length > 200 || state is not ("all" or "active" or "inactive"))
            return BadRequest("Bộ lọc tài khoản không hợp lệ.");

        keyword = keyword?.Trim();
        var query = Db.Set<AccountUser>().AsNoTracking();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(user => user.UserName.Contains(keyword)
                || (user.Email != null && user.Email.Contains(keyword)));
        if (state != "all") query = query.Where(user => user.IsActive == (state == "active"));
        if (roleId.HasValue) query = query.Where(user => user.Roles.Any(role => role.Id == roleId.Value));

        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / 10d));
        pageNumber = Math.Clamp(pageNumber, 1, pages);
        var items = await query.OrderByDescending(user => user.CreatedAtUtc).ThenByDescending(user => user.Id)
            .Skip((pageNumber - 1) * 10).Take(10)
            .Select(user => new AccountRow(user.Id, user.UserName, user.Email, user.IsActive,
                user.CreatedAtUtc, user.Roles.OrderBy(role => role.Name).Select(role => role.Name).ToList()))
            .ToListAsync(cancellationToken);

        var filters = new Dictionary<string, string> { ["keyword"] = keyword ?? "", ["state"] = state };
        if (roleId.HasValue) filters["roleId"] = roleId.Value.ToString(CultureInfo.InvariantCulture);
        return View(new AccountsIndexModel(items, keyword, state, roleId,
            await RolesAsync(cancellationToken), total, ActorId,
            new ManagementPager("AdminAccounts", pageNumber, pages, filters)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var account = await Db.Set<AccountUser>().AsNoTracking()
            .Include(user => user.Roles).Include(user => user.UserProfile)
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (account is null) return NotFound();

        return View(new AccountDetailsModel(new AccountRow(account.Id, account.UserName,
            account.Email, account.IsActive, account.CreatedAtUtc,
            account.Roles.Select(role => role.Name).OrderBy(name => name).ToList()),
            account.UserProfile?.FullName, account.UserProfile?.PhoneNumber,
            account.UserProfile?.DateOfBirth?.ToString("dd/MM/yyyy"), account.UserProfile?.Gender,
            account.EmailConfirmed, Convert.ToBase64String(account.Version), ActorId));
    }

    [HttpGet("{id:long}/edit")]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        if (id == ActorId) return Forbid();
        var account = await Db.Set<AccountUser>().AsNoTracking().Include(user => user.Roles)
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (account is null) return NotFound();
        return View(new AccountEditInput
        {
            Id = id, UserName = account.UserName, IsActive = account.IsActive,
            Version = Convert.ToBase64String(account.Version),
            RoleIds = account.Roles.Select(role => role.Id).ToArray(),
            AvailableRoles = await RolesAsync(cancellationToken)
        });
    }

    [HttpPost("{id:long}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] long id, AccountEditInput input,
        CancellationToken cancellationToken)
    {
        if (id == ActorId) return Forbid();
        input.Id = id;
        input.RoleIds = (input.RoleIds ?? Array.Empty<long>()).Distinct().ToArray();
        if (input.RoleIds.Length is 0 or > 100)
            ModelState.AddModelError(nameof(input.RoleIds), "Hãy chọn ít nhất một vai trò hợp lệ.");

        if (ModelState.IsValid)
        {
            var error = await ChangeAccountAsync(id, input, false, cancellationToken);
            if (error is null)
            {
                TempData["ManagementSuccess"] = "Đã cập nhật trạng thái và vai trò tài khoản.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(string.Empty, error);
        }

        input.UserName = await Db.Set<AccountUser>().AsNoTracking().Where(user => user.Id == id)
            .Select(user => user.UserName).SingleOrDefaultAsync(cancellationToken) ?? "Tài khoản";
        input.AvailableRoles = await RolesAsync(cancellationToken);
        return View(input);
    }

    [HttpPost("{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, string? version, CancellationToken cancellationToken)
    {
        if (id == ActorId) return Forbid();
        var error = await ChangeAccountAsync(id,
            new AccountEditInput { Version = version ?? "" }, true, cancellationToken);
        TempData[error is null ? "ManagementSuccess" : "ManagementError"] =
            error ?? "Đã xóa tài khoản chưa có dữ liệu nghiệp vụ liên quan.";
        return RedirectToAction(nameof(Index));
    }

    private Task<List<AccountRoleOption>> RolesAsync(CancellationToken cancellationToken) =>
        Db.Set<Role>().AsNoTracking().OrderBy(role => role.Name)
            .Select(role => new AccountRoleOption(role.Id, role.Code, role.Name))
            .ToListAsync(cancellationToken);

    private async Task<string?> ChangeAccountAsync(long id, AccountEditInput input, bool delete,
        CancellationToken cancellationToken)
    {
        var version = new byte[8];
        if (!Convert.TryFromBase64String(input.Version ?? "", version, out var length) || length != 8)
            return "Phiên sửa không hợp lệ. Hãy mở lại tài khoản.";

        try
        {
            return await Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                Db.ChangeTracker.Clear();
                await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

                // Cùng khóa một dòng vai trò để hai thao tác quản trị tài khoản chạy lần lượt.
                var adminRoleId = await Db.Database.SqlQuery<long>($"""
                    SELECT Id AS [Value] FROM dbo.Roles WITH (UPDLOCK, HOLDLOCK) WHERE Code = 'ADMIN'
                    """).SingleOrDefaultAsync(cancellationToken);
                if (adminRoleId == 0) return "Chưa có vai trò ADMIN trong cơ sở dữ liệu.";

                var actorIsValid = await Db.Set<AccountUser>().AsNoTracking().AnyAsync(user =>
                    user.Id == ActorId && user.IsActive && user.SecurityStamp == ActorStamp
                    && user.Roles.Any(role => role.Code == "ADMIN"), cancellationToken);
                if (!actorIsValid) return "Phiên quản trị đã thay đổi. Hãy đăng nhập lại.";

                var account = await Db.Set<AccountUser>().AsTracking().Include(user => user.Roles)
                    .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
                if (account is null) return "Tài khoản đã bị xóa.";
                if (!account.Version.AsSpan().SequenceEqual(version))
                    return "Tài khoản vừa thay đổi. Hãy mở bản mới nhất để đối chiếu.";

                var roles = delete ? new List<Role>() : await Db.Set<Role>()
                    .Where(role => input.RoleIds.Contains(role.Id)).ToListAsync(cancellationToken);
                if (!delete && roles.Count != input.RoleIds.Length)
                    return "Danh sách vai trò đã thay đổi. Hãy tải lại trang.";

                var wasActiveAdmin = account.IsActive && account.Roles.Any(role => role.Code == "ADMIN");
                var remainsActiveAdmin = !delete && input.IsActive && roles.Any(role => role.Code == "ADMIN");
                if (wasActiveAdmin && !remainsActiveAdmin && !await Db.Set<AccountUser>().AnyAsync(user =>
                    user.Id != id && user.IsActive && user.Roles.Any(role => role.Code == "ADMIN"),
                    cancellationToken))
                    return "Cần giữ ít nhất một tài khoản ADMIN đang hoạt động.";

                account.Roles.Clear();
                if (delete)
                {
                    // Chỉ dọn dữ liệu phụ thuộc của hồ sơ. Khóa ngoại bảo vệ đơn, hóa đơn và lịch sử.
                    await Db.Database.ExecuteSqlInterpolatedAsync($"""
                        DELETE FROM dbo.UserTokens WHERE UserId = {id};
                        DELETE FROM dbo.UserAddresses WHERE UserId = {id};
                        DELETE FROM dbo.UserProfiles WHERE UserId = {id};
                        """, cancellationToken);
                    Db.Set<AccountUser>().Remove(account);
                }
                else
                {
                    foreach (var role in roles) account.Roles.Add(role);
                    account.IsActive = input.IsActive;
                    if (input.IsActive)
                    {
                        account.AccessFailedCount = 0;
                        account.LockoutEndUtc = null;
                    }
                    account.SecurityStamp = Guid.NewGuid();
                    account.UpdatedAtUtc = DateTime.UtcNow;
                }

                RecordAudit(delete ? "users.delete" : "users.access.update", "Users", id,
                    delete ? "Xóa tài khoản chưa có dữ liệu nghiệp vụ."
                    : $"Hoạt động: {input.IsActive}; vai trò: {string.Join(", ", roles.Select(role => role.Code))}.");
                await Db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (string?)null;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return "Tài khoản vừa thay đổi. Hãy mở lại trước khi lưu.";
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 547 })
        {
            return "Tài khoản có dữ liệu liên quan nên chưa thể xóa. Anh có thể khóa tài khoản.";
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return "Dữ liệu tài khoản vừa thay đổi. Hãy tải lại trang.";
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            return "Tài khoản đang được cập nhật. Hãy tải lại trang rồi thử lại.";
        }
    }
}

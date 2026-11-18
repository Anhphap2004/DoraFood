using DoraFood.Areas.Admin.Filters;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccountUser = DoraFood.Models.User;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN")]
[RequirePermission("users.manage")]
[Route("admin/accounts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminAccountsController : AdminModuleController
{
    private const int PageSize = 10;

    protected override string[] AllowedRoles => new[] { "ADMIN" };

    public AdminAccountsController(DoraFoodContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken,
        string? keyword = null,
        string state = "all",
        long? roleId = null,
        int pageNumber = 1)
    {
        keyword = keyword?.Trim();

        if (!ModelState.IsValid
            || keyword?.Length > 200
            || state is not ("all" or "active" or "inactive")
            || roleId is <= 0)
        {
            return BadRequest("Bộ lọc tài khoản không hợp lệ.");
        }

        var query = Db.Set<AccountUser>().AsNoTracking();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(account =>
                account.UserName.Contains(keyword)
                || (account.Email != null && account.Email.Contains(keyword)));
        }

        if (state != "all")
        {
            var isActive = state == "active";
            query = query.Where(account => account.IsActive == isActive);
        }

        if (roleId.HasValue)
        {
            query = query.Where(account =>
                account.Roles.Any(role => role.Id == roleId.Value));
        }

        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var accounts = await query
            .OrderByDescending(account => account.CreatedAtUtc)
            .ThenByDescending(account => account.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(account => new AccountRow(
                account.Id,
                account.UserName,
                account.Email,
                account.IsActive,
                account.CreatedAtUtc,
                account.Roles
                    .OrderBy(role => role.Name)
                    .Select(role => role.Name)
                    .ToList()))
            .ToListAsync(cancellationToken);

        // Giữ bộ lọc khi chuyển trang.
        var filters = new Dictionary<string, string> { ["state"] = state };

        if (!string.IsNullOrEmpty(keyword))
            filters["keyword"] = keyword;

        if (roleId.HasValue)
            filters["roleId"] = roleId.Value.ToString(CultureInfo.InvariantCulture);

        var roles = await RolesAsync(cancellationToken);
        var pager = new ManagementPager(
            "AdminAccounts", pageNumber, totalPages, filters);

        return View(new AccountsIndexModel(
            accounts, keyword, state, roleId, roles, total, ActorId, pager));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(
        long id,
        CancellationToken cancellationToken)
    {
        var account = await Db.Set<AccountUser>()
            .AsNoTracking()
            .Include(item => item.Roles)
            .Include(item => item.UserProfile)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (account is null)
            return NotFound();

        var accountRow = new AccountRow(
            account.Id,
            account.UserName,
            account.Email,
            account.IsActive,
            account.CreatedAtUtc,
            account.Roles
                .OrderBy(role => role.Name)
                .Select(role => role.Name)
                .ToList());

        var profile = account.UserProfile;

        return View(new AccountDetailsModel(
            accountRow,
            profile?.FullName,
            profile?.PhoneNumber,
            profile?.DateOfBirth?.ToString("dd/MM/yyyy"),
            profile?.Gender,
            account.EmailConfirmed,
            Convert.ToBase64String(account.Version),
            ActorId));
    }

    [HttpGet("{id:long}/edit")]
    public async Task<IActionResult> Edit(
        long id,
        CancellationToken cancellationToken)
    {
        if (id == ActorId)
            return Forbid();

        var account = await Db.Set<AccountUser>()
            .AsNoTracking()
            .Include(item => item.Roles)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (account is null)
            return NotFound();

        return View(new AccountEditInput
        {
            Id = account.Id,
            UserName = account.UserName,
            IsActive = account.IsActive,
            Version = Convert.ToBase64String(account.Version),
            RoleIds = account.Roles.Select(role => role.Id).ToArray(),
            AvailableRoles = await RolesAsync(cancellationToken)
        });
    }

    [HttpPost("{id:long}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        AccountEditInput input,
        CancellationToken cancellationToken)
    {
        if (id == ActorId)
            return Forbid();

        input.Id = id;
        input.RoleIds = (input.RoleIds ?? Array.Empty<long>())
            .Distinct()
            .ToArray();

        if (input.RoleIds.Length is 0 or > 100
            || input.RoleIds.Any(roleId => roleId <= 0))
        {
            ModelState.AddModelError(
                nameof(input.RoleIds),
                "Hãy chọn ít nhất một vai trò hợp lệ.");
        }

        if (ModelState.IsValid)
        {
            var error = await ChangeAccountAsync(
                id, input, delete: false, cancellationToken: cancellationToken);

            if (error is null)
            {
                TempData["ManagementSuccess"] =
                    "Đã lưu vai trò và trạng thái tài khoản.";

                return RedirectToAction(nameof(Details), new { id });
            }

            ModelState.AddModelError(string.Empty, error);
        }

        var userName = await Db.Set<AccountUser>()
            .AsNoTracking()
            .Where(account => account.Id == id)
            .Select(account => account.UserName)
            .SingleOrDefaultAsync(cancellationToken);

        if (userName is null)
            return NotFound();

        input.UserName = userName;
        input.AvailableRoles = await RolesAsync(cancellationToken);

        return View(input);
    }

    [HttpPost("{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        [FromRoute] long id,
        [FromForm] string? version,
        CancellationToken cancellationToken)
    {
        if (id == ActorId)
            return Forbid();

        var error = await ChangeAccountAsync(
            id,
            new AccountEditInput { Version = version ?? string.Empty },
            delete: true,
            cancellationToken: cancellationToken);

        if (error is not null)
        {
            TempData["ManagementError"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["ManagementSuccess"] = "Đã xóa tài khoản.";
        return RedirectToAction(nameof(Index));
    }

    private Task<List<AccountRoleOption>> RolesAsync(
        CancellationToken cancellationToken)
    {
        return Db.Set<Role>()
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .ThenBy(role => role.Id)
            .Select(role => new AccountRoleOption(role.Id, role.Code, role.Name))
            .ToListAsync(cancellationToken);
    }

    private async Task<string?> ChangeAccountAsync(
        long id,
        AccountEditInput input,
        bool delete,
        CancellationToken cancellationToken)
    {
        var version = new byte[8];

        if (!Convert.TryFromBase64String(
                input.Version ?? string.Empty, version, out var length)
            || length != version.Length)
        {
            return "Phiên sửa không hợp lệ. Hãy mở lại tài khoản.";
        }

        try
        {
            return await Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                Db.ChangeTracker.Clear();

                await using var transaction =
                    await Db.Database.BeginTransactionAsync(cancellationToken);

                // Các thao tác sửa và xóa tài khoản lần lượt lấy cùng khóa này.
                var adminRoleId = await Db.Database.SqlQueryRaw<long>("""
                    SELECT Id AS [Value]
                    FROM dbo.Roles WITH (UPDLOCK, HOLDLOCK)
                    WHERE Code = 'ADMIN'
                    """).SingleOrDefaultAsync(cancellationToken);

                if (adminRoleId == 0)
                    return "Chưa có vai trò ADMIN trong cơ sở dữ liệu.";

                var actorIsValid = await Db.Set<AccountUser>().AsNoTracking()
                    .AnyAsync(account =>
                        account.Id == ActorId
                        && account.IsActive
                        && account.SecurityStamp == ActorStamp
                        && account.Roles.Any(role => role.Id == adminRoleId),
                        cancellationToken);

                if (!actorIsValid)
                    return "Phiên quản trị đã thay đổi. Hãy đăng nhập lại.";

                var account = await Db.Set<AccountUser>()
                    .AsTracking()
                    .Include(item => item.Roles)
                    .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

                if (account is null)
                    return "Tài khoản đã bị xóa.";

                if (!account.Version.AsSpan().SequenceEqual(version))
                    return "Tài khoản vừa thay đổi. Hãy mở bản mới nhất trước khi lưu.";

                var selectedRoles = delete
                    ? new List<Role>()
                    : await Db.Set<Role>()
                        .AsTracking()
                        .Where(role => input.RoleIds.Contains(role.Id))
                        .ToListAsync(cancellationToken);

                if (!delete && selectedRoles.Count != input.RoleIds.Length)
                    return "Danh sách vai trò đã thay đổi. Hãy tải lại trang.";

                var currentRoleIds = account.Roles.Select(role => role.Id).ToHashSet();
                var selectedRoleIds = selectedRoles.Select(role => role.Id).ToHashSet();

                var wasActiveAdmin = account.IsActive && currentRoleIds.Contains(adminRoleId);
                var remainsActiveAdmin = !delete && input.IsActive
                    && selectedRoleIds.Contains(adminRoleId);

                if (wasActiveAdmin && !remainsActiveAdmin)
                {
                    var anotherAdminExists = await Db.Set<AccountUser>()
                        .AsNoTracking()
                        .AnyAsync(item =>
                            item.Id != id
                            && item.IsActive
                            && item.Roles.Any(role => role.Id == adminRoleId),
                            cancellationToken);

                    if (!anotherAdminExists)
                        return "Cần giữ ít nhất một tài khoản ADMIN đang hoạt động.";
                }

                if (delete)
                {
                    account.Roles.Clear();

                    await Db.Database.ExecuteSqlInterpolatedAsync($"""
                        DELETE FROM dbo.UserTokens WHERE UserId = {id};
                        DELETE FROM dbo.UserAddresses WHERE UserId = {id};
                        DELETE FROM dbo.UserProfiles WHERE UserId = {id};
                        """, cancellationToken);

                    Db.Set<AccountUser>().Remove(account);

                    RecordAudit(
                        "users.delete", "Users", id,
                        "Xóa tài khoản chưa có dữ liệu nghiệp vụ tham chiếu.");
                }
                else
                {
                    var statusChanged = account.IsActive != input.IsActive;
                    var rolesChanged = !currentRoleIds.SetEquals(selectedRoleIds);

                    if (!statusChanged && !rolesChanged)
                        return (string?)null;

                    foreach (var role in account.Roles
                        .Where(role => !selectedRoleIds.Contains(role.Id))
                        .ToList())
                    {
                        account.Roles.Remove(role);
                    }

                    foreach (var role in selectedRoles
                        .Where(role => !currentRoleIds.Contains(role.Id)))
                    {
                        account.Roles.Add(role);
                    }

                    account.IsActive = input.IsActive;

                    if (statusChanged && input.IsActive)
                    {
                        account.AccessFailedCount = 0;
                        account.LockoutEndUtc = null;
                    }

                    account.SecurityStamp = Guid.NewGuid();
                    account.UpdatedAtUtc = DateTime.UtcNow;

                    var roleCodes = string.Join(", ",
                        selectedRoles.Select(role => role.Code).OrderBy(code => code));

                    RecordAudit(
                        "users.access.update", "Users", id,
                        $"Trạng thái: {(input.IsActive ? "Hoạt động" : "Đã khóa")}. "
                        + $"Vai trò: {roleCodes}.");
                }

                await Db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return (string?)null;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return "Tài khoản vừa thay đổi. Hãy mở lại trước khi lưu.";
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 547 })
        {
            return delete
                ? "Tài khoản có dữ liệu liên quan. Anh có thể khóa tài khoản."
                : "Dữ liệu vai trò vừa thay đổi. Hãy tải lại trang.";
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return delete
                ? "Tài khoản có dữ liệu liên quan. Anh có thể khóa tài khoản."
                : "Dữ liệu tài khoản vừa thay đổi. Hãy tải lại trang.";
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            return "Tài khoản đang được cập nhật. Hãy tải lại trang rồi thử lại.";
        }
    }
}
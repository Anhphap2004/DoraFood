using DoraFood.Areas.Admin.Filters;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN")]
[RequirePermission("roles.manage")]
[Route("admin/permissions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPermissionsController : AdminModuleController
{
    protected override string[] AllowedRoles => new[] { "ADMIN" };

    public AdminPermissionsController(DoraFoodContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken,
        long? roleId = null)
    {
        if (!ModelState.IsValid)
            return BadRequest("Mã vai trò không hợp lệ.");

        var roles = await Db.Set<Role>()
            .AsNoTracking()
            .Include(role => role.Permissions)
            .OrderBy(role => role.Code)
            .ToListAsync(cancellationToken);

        var selectedRole = roleId.HasValue
            ? roles.FirstOrDefault(role => role.Id == roleId.Value)
            : roles.FirstOrDefault();

        if (roleId.HasValue && selectedRole == null)
            return NotFound("Không tìm thấy vai trò.");

        var assignedIds = selectedRole?.Permissions
            .Select(permission => permission.Id)
            .ToHashSet() ?? new HashSet<long>();

        var permissions = await Db.Set<Permission>()
            .AsNoTracking()
            .OrderBy(permission => permission.Code)
            .ToListAsync(cancellationToken);

        var rows = permissions
            .Select(permission => new PermissionRow(
                permission.Id,
                permission.Code,
                permission.Name,
                GroupName(permission.Code),
                assignedIds.Contains(permission.Id)))
            .ToList();

        return View(new PageModel(roles, selectedRole, rows));
    }
    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
[FromForm] long roleId,
[FromForm] long[]? permissionIds,
CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || roleId <= 0)
            return BadRequest("Dữ liệu phân quyền không hợp lệ.");

        var selectedIds = (permissionIds ?? Array.Empty<long>())
            .Distinct()
            .ToArray();

        if (selectedIds.Any(id => id <= 0))
            return BadRequest("Có mã quyền không hợp lệ.");

        await using var transaction = await Db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var role = await Db.Set<Role>()
            .AsTracking()
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId, cancellationToken);

        if (role == null)
            return NotFound("Không tìm thấy vai trò.");

        if (role.Code is "ADMIN" or "CUSTOMER")
            return Forbid();

        var selectedPermissions = await Db.Set<Permission>()
            .AsTracking()
            .Where(permission => selectedIds.Contains(permission.Id))
            .ToListAsync(cancellationToken);

        if (selectedPermissions.Count != selectedIds.Length)
            return BadRequest("Có quyền không còn tồn tại. Anh hãy tải lại trang.");

        if (selectedPermissions.Any(permission =>
                permission.Code == "roles.manage"
                || permission.Code == "users.manage"))
        {
            return BadRequest("Quyền quản lý tài khoản và phân quyền dành riêng cho ADMIN.");
        }

        var selectedIdSet = selectedIds.ToHashSet();

        // Gỡ các quyền đã bỏ chọn.
        foreach (var permission in role.Permissions
            .Where(permission => !selectedIdSet.Contains(permission.Id))
            .ToList())
        {
            role.Permissions.Remove(permission);
        }

        // Thêm các quyền mới được chọn.
        var existingIds = role.Permissions
            .Select(permission => permission.Id)
            .ToHashSet();

        foreach (var permission in selectedPermissions)
        {
            if (!existingIds.Contains(permission.Id))
                role.Permissions.Add(permission);
        }

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["PermissionSuccess"] = $"Đã lưu quyền cho vai trò {role.Name}.";
        return RedirectToAction(nameof(Index), new { roleId });
    }
    private static string GroupName(string code) =>
        code.Split('.')[0] switch
        {
            "orders" or "reservations" or "kitchen" => "Vận hành nhà hàng",
            "invoices" or "payments" or "refunds" => "Hóa đơn & thanh toán",
            "inventory" => "Kho",
            "content" or "menu" => "Website & thực đơn",
            "users" or "roles" => "Tài khoản & phân quyền",
            "reports" => "Báo cáo",
            _ => "Quyền khác"
        };

    public sealed record PermissionRow(
        long Id,
        string Code,
        string Name,
        string Group,
        bool Assigned);

    public sealed record PageModel(
        IReadOnlyList<Role> Roles,
        Role? SelectedRole,
        IReadOnlyList<PermissionRow> Permissions);
}
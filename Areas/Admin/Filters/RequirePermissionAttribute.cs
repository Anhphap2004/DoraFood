using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using AccountUser = DoraFood.Models.User;

namespace DoraFood.Areas.Admin.Filters;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = true)]
public sealed class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(string permissionCode)
        : base(typeof(RequirePermissionFilter))
    {
        Arguments = new object[] { permissionCode };
    }
}

public sealed class RequirePermissionFilter : IAsyncAuthorizationFilter
{
    private readonly DoraFoodContext _db;
    private readonly string _permissionCode;

    public RequirePermissionFilter(
        DoraFoodContext db,
        string permissionCode)
    {
        _db = db;
        _permissionCode = permissionCode;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        if (!long.TryParse(
                user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || userId <= 0
            || !Guid.TryParse(
                user.FindFirstValue("SecurityStamp"), out var securityStamp))
        {
            context.Result = new ForbidResult();
            return;
        }

        var allowed = await _db.Set<AccountUser>()
            .AsNoTracking()
            .AnyAsync(
                account => account.Id == userId
                    && account.IsActive
                    && account.SecurityStamp == securityStamp
                    && account.Roles.Any(role =>
                        role.Code == "ADMIN"
                        || role.Permissions.Any(permission =>
                            permission.Code == _permissionCode)),
                context.HttpContext.RequestAborted);

        if (!allowed)
        {
            context.Result = new ForbidResult();
        }
    }
}
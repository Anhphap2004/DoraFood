using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Areas.Admin.Controllers;

public abstract class AdminModuleController : Controller
{
    protected DoraFoodContext Db { get; }
    protected long ActorId { get; private set; }
    protected Guid ActorStamp { get; private set; }
    protected virtual string[] AllowedRoles => new[] { "ADMIN", "MANAGER" };

    protected AdminModuleController(DoraFoodContext db) => Db = db;

    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var roles = AllowedRoles;
        var claimsAreValid = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            && id > 0;
        var stampIsValid = Guid.TryParse(User.FindFirstValue("SecurityStamp"), out var stamp);

        if (!claimsAreValid || !stampIsValid || !await Db.Set<DoraFood.Models.User>()
            .AsNoTracking().AnyAsync(user => user.Id == id && user.IsActive
                && user.SecurityStamp == stamp && user.Roles.Any(role => roles.Contains(role.Code)),
                HttpContext.RequestAborted))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result = RedirectToAction("Login", "Account", new { area = "" });
            return;
        }

        ActorId = id;
        ActorStamp = stamp;
        await next();
    }

    protected void RecordAudit(string action, string entity, long id, string summary)
    {
        Db.Set<AuditLog>().Add(new AuditLog
        {
            ActorId = ActorId,
            Action = action,
            EntityName = entity,
            EntityKey = id.ToString(CultureInfo.InvariantCulture),
            Summary = summary,
            CreatedAtUtc = DateTime.UtcNow
        });
    }
}

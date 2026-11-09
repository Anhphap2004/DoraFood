using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.ViewComponents;

public sealed class CheckoutAddressesViewComponent : ViewComponent
{
    private readonly DoraFoodContext _context;

    public CheckoutAddressesViewComponent(DoraFoodContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userIdClaim = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (HttpContext.User.Identity?.IsAuthenticated != true
            || !long.TryParse(userIdClaim, out var userId)
            || userId <= 0)
        {
            return Content(string.Empty);
        }

        var addresses = await _context.Set<UserAddress>()
            .AsNoTracking()
            .Where(address => address.UserId == userId)
            .OrderByDescending(address => address.IsDefault)
            .ThenBy(address => address.Id)
            .Select(address => new AddressOption(
                address.Id,
                address.RecipientName,
                address.PhoneNumber,
                address.AddressLine,
                address.IsDefault))
            .ToListAsync(HttpContext.RequestAborted);

        if (addresses.Count == 0)
        {
            return Content(string.Empty);
        }

        return View(addresses);
    }

    public sealed record AddressOption(
        long Id,
        string RecipientName,
        string PhoneNumber,
        string AddressLine,
        bool IsDefault);
}
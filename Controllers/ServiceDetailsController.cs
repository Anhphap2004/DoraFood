using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

[AllowAnonymous]
public sealed class ServiceDetailsController : Controller
{
    private readonly DoraFoodContext _context;

    public ServiceDetailsController(DoraFoodContext context)
    {
        _context = context;
    }
    [HttpGet("/dich-vu", Name = "DoraFoodServices")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var services = await _context.Set<Service>()
            .AsNoTracking()
            .Where(service => service.IsActive)
            .OrderBy(service => service.Name)
            .ThenBy(service => service.Id)
            .Select(service => new ServiceListItem(
                service.Name,
                service.Slug,
                service.BasePrice,
                service.ImageUrl))
            .ToListAsync(cancellationToken);

        return View(services);
    }

    public sealed record ServiceListItem(
        string Name,
        string Slug,
        decimal BasePrice,
        string? ImageUrl);
    [HttpGet("/dich-vu/{slug}", Name = "DoraFoodServiceDetails")]
    public async Task<IActionResult> Details(
        [FromRoute] string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 200)
        {
            return NotFound();
        }

        var service = await _context.Set<Service>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.IsActive && item.Slug == slug,
                cancellationToken);

        if (service is null)
        {
            return NotFound();
        }

        var imagePath = service.ImageUrl?.Trim();
        var imageUrl = Url.IsLocalUrl(imagePath) ? Url.Content(imagePath) : null;

        var description = string.IsNullOrWhiteSpace(service.DescriptionHtml)
            ? null
            : new HtmlSanitizer().Sanitize(service.DescriptionHtml);

        return View(new ServiceDetailsModel(
            service.Name, description, service.BasePrice, imageUrl));
    }

    public sealed record ServiceDetailsModel(
        string Name, string? DescriptionHtml, decimal BasePrice, string? ImageUrl);
}

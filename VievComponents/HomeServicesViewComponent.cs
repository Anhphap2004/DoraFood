using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DoraFood.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.ViewComponents;

public sealed class HomeServicesViewComponent : ViewComponent
{
    private readonly DoraFoodContext _context;

    public HomeServicesViewComponent(DoraFoodContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var services = await _context.Services
            .AsNoTracking()
            .Where(service => service.IsActive)
            .OrderBy(service => service.Id)
            .Select(service => new
            {
                service.Name,
                service.Slug,
                service.DescriptionHtml,
                service.BasePrice,
                service.ImageUrl
            })
            .ToListAsync(HttpContext.RequestAborted);

        if (services.Count == 0)
        {
            return Content(string.Empty);
        }

        var fallbackPhoto = Url.Content("~/assets/images/p1.jpg");
        var cards = new List<ServiceCard>(services.Count);
        foreach (var service in services)
        {
            var (photoIndex, icon, occasion) = service.Slug switch
            {
                "sum-vay-gia-dinh" => (0, "fa-utensils", "Gia đình"),
                "khai-xuan-cung-ban" => (1, "fa-glass-cheers", "Bạn bè"),
                "dip-vui-dau-nam" => (2, "fa-birthday-cake", "Sinh nhật"),
                "gan-ket-dong-nghiep" => (3, "fa-building", "Doanh nghiệp"),
                _ => (-1, "fa-concierge-bell", (string?)null)
            };
            var imageUrl = service.ImageUrl?.Trim();

            var photo = !string.IsNullOrWhiteSpace(imageUrl) && Url.IsLocalUrl(imageUrl)
                ? Url.Content(imageUrl)
                : Url.Content("~/assets/images/p1.jpg");

            // Mô tả trên thẻ chỉ cần văn bản để giữ kích thước thẻ đồng đều.
            var description = Regex.Replace(service.DescriptionHtml ?? string.Empty, "<[^<>]*>", " ");
            description = Regex.Replace(WebUtility.HtmlDecode(description), @"\s+", " ").Trim();
            cards.Add(new ServiceCard(service.Name, description, service.BasePrice, photo, icon, occasion));
        }

        return View(cards);
    }

    public sealed record ServiceCard(
        string Name,
        string Description,
        decimal BasePrice,
        string PhotoUrl,
        string Icon,
        string? Occasion);
}

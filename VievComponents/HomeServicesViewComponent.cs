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

    public async Task<IViewComponentResult> InvokeAsync(string[]? photos = null)
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
                service.ImageUrl,
                service.BasePrice
            })
            .ToListAsync(HttpContext.RequestAborted);

        if (services.Count == 0)
        {
            return Content(string.Empty);
        }

        var fallbackPhoto = Url.Content("~/assets/images/p1.jpg")!;
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
            var imagePath = service.ImageUrl?.Trim();
            var photo = Url.IsLocalUrl(imagePath)
                ? Url.Content(imagePath)
                : photos is not null && photoIndex >= 0 && photoIndex < photos.Length
                    ? photos[photoIndex]
                    : fallbackPhoto;

            // Mô tả trên thẻ chỉ cần văn bản để giữ kích thước thẻ đồng đều.
            var description = Regex.Replace(service.DescriptionHtml ?? string.Empty, "<[^<>]*>", " ");
            description = Regex.Replace(WebUtility.HtmlDecode(description), @"\s+", " ").Trim();
            cards.Add(new ServiceCard(service.Name, description, service.BasePrice,
                photo ?? fallbackPhoto, icon, occasion) { Slug = service.Slug });
        }

        return View(cards);
    }

    public sealed record ServiceCard(
        string Name,
        string Description,
        decimal BasePrice,
        string PhotoUrl,
        string Icon,
        string? Occasion)
    {
        public string Slug { get; init; } = string.Empty;
    }
}

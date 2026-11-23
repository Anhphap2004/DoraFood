using System.Linq;
using System.Threading.Tasks;
using DoraFood.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.ViewComponents;

public sealed class CustomerFaqViewComponent : ViewComponent
{
    private readonly DoraFoodContext _db;

    public CustomerFaqViewComponent(DoraFoodContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync(string menuPageUrl)
    {
        var items = await _db.FaqItems
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToListAsync(HttpContext.RequestAborted);

        if (items.Count == 0)
        {
            return Content(string.Empty);
        }

        ViewData["MenuPageUrl"] = menuPageUrl;
        return View(items);
    }
}
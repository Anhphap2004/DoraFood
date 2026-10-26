using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.ViewComponents;

public class MainMenuViewComponent : ViewComponent
{
    private readonly DoraFoodContext _context;

    public MainMenuViewComponent(DoraFoodContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var items = await _context.Set<NavigationItem>()
            .AsNoTracking()
            .Where(item =>
                item.Menu.Code == "HEADER"
                && item.IsVisible)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToListAsync(HttpContext.RequestAborted);

        return View(items);
    }
}
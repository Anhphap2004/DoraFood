using DoraFood.Areas.Admin.Filters;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[RequirePermission("content.manage")]
[AutoValidateAntiforgeryToken]
[Route("admin/faq")]
public sealed class FaqController : Controller
{
    private readonly DoraFoodContext _db;

    public FaqController(DoraFoodContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _db.FaqItems
            .AsNoTracking()
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        return View(items);
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        return View("Form", new FaqInput());
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(
        FaqInput input, CancellationToken cancellationToken)
    {
        input.Id = 0;

        if (!ModelState.IsValid)
            return View("Form", input);

        var item = new FaqItem();
        ApplyInput(item, input);

        _db.FaqItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["FaqSuccess"] = "Đã thêm câu hỏi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:long}/edit")]
    public async Task<IActionResult> Edit(
        [FromRoute] long id, CancellationToken cancellationToken)
    {
        var item = await _db.FaqItems.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (item is null)
            return NotFound();

        return View("Form", new FaqInput
        {
            Id = item.Id,
            Question = item.Question,
            Answer = item.Answer,
            SortOrder = item.SortOrder,
            IsActive = item.IsActive,
            Version = Convert.ToBase64String(item.Version)
        });
    }

    [HttpPost("{id:long}/edit")]
    public async Task<IActionResult> Edit(
        [FromRoute] long id, FaqInput input, CancellationToken cancellationToken)
    {
        input.Id = id;

        if (!ModelState.IsValid)
            return View("Form", input);

        var version = new byte[8];

        if (input.Version is not { Length: 12 }
            || !Convert.TryFromBase64String(input.Version, version, out var written)
            || written != version.Length)
        {
            return BadRequest("Phiên chỉnh sửa không hợp lệ. Hãy mở lại câu hỏi.");
        }

        var item = await _db.FaqItems
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (item is null)
            return NotFound();

        _db.Entry(item).Property(entry => entry.Version).OriginalValue = version;
        ApplyInput(item, input);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["FaqError"] =
                "Câu hỏi vừa được người khác cập nhật. Hãy xem nội dung mới nhất rồi sửa lại.";

            return RedirectToAction(nameof(Edit), new { id });
        }

        TempData["FaqSuccess"] = "Đã cập nhật câu hỏi.";
        return RedirectToAction(nameof(Index));
    }

    private static void ApplyInput(FaqItem item, FaqInput input)
    {
        item.Question = input.Question.Trim();
        item.Answer = input.Answer.Trim();
        item.SortOrder = input.SortOrder;
        item.IsActive = input.IsActive;
    }
}
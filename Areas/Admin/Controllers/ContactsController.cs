using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[Route("admin/contacts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContactsController : Controller
{
    private readonly DoraFoodContext _context;

    public ContactsController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken,
        string? keyword = null,
        string? status = null,
        int pageNumber = 1)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Thông tin tìm kiếm không hợp lệ.");
        }

        keyword = keyword?.Trim();
        status = status?.Trim().ToUpperInvariant();

        if (keyword?.Length > 200)
        {
            return BadRequest("Từ khóa tối đa 200 ký tự.");
        }

        if (!string.IsNullOrEmpty(status)
            && status is not ("NEW" or "PROCESSING" or "CLOSED"))
        {
            return BadRequest("Trạng thái liên hệ không hợp lệ.");
        }

        var query = _context.Set<ContactMessage>().AsNoTracking();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(message =>
                message.FullName.Contains(keyword)
                || message.Subject.Contains(keyword)
                || (message.Email != null && message.Email.Contains(keyword))
                || (message.PhoneNumber != null
                    && message.PhoneNumber.Contains(keyword)));
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(message => message.Status == status);
        }

        const int pageSize = 10;

        var totalMessages = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(
            1, (int)Math.Ceiling(totalMessages / (double)pageSize));

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var messages = await query
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenByDescending(message => message.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(message => new ContactMessageRow(
                message.Id,
                message.FullName,
                message.Email,
                message.PhoneNumber,
                message.Subject,
                message.Status,
                message.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return View(new ContactMessagesViewModel(
            messages,
            keyword,
            status,
            pageNumber,
            totalPages,
            totalMessages));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        var message = await _context.Set<ContactMessage>()
    .AsNoTracking()
    .Include(item => item.AssignedToNavigation)
    .SingleOrDefaultAsync(
        item => item.Id == id,
        cancellationToken);

        if (message is null)
        {
            return NotFound();
        }

        return View(message);
    }
    [HttpPost("{id:long}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(
    [FromRoute] long id,
    [FromForm] string? expectedStatus,
    [FromForm] string? targetStatus,
    CancellationToken cancellationToken)
    {
        var isAllowedTransition =
            (expectedStatus, targetStatus) is
            ("NEW", "PROCESSING") or ("PROCESSING", "CLOSED");

        if (!ModelState.IsValid || !isAllowedTransition)
        {
            return BadRequest("Thao tác cập nhật trạng thái không hợp lệ.");
        }

        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(claim, out var actorId))
        {
            return Forbid();
        }

        var changedRows = await _context.Set<ContactMessage>()
            .Where(message =>
                message.Id == id
                && message.Status == expectedStatus
                && _context.Set<DoraFood.Models.User>().Any(user =>
                    user.Id == actorId && user.IsActive))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, targetStatus!)
                .SetProperty(
                    message => message.AssignedTo,
                    message => targetStatus == "PROCESSING"
                        ? (long?)actorId
                        : message.AssignedTo),
                cancellationToken);

        if (changedRows == 0)
        {
            TempData["ContactError"] =
                "Liên hệ đã thay đổi, đã bị xóa hoặc phiên làm việc không còn hợp lệ. "
                + "Anh hãy kiểm tra lại danh sách.";

            return RedirectToAction(nameof(Index));
        }

        TempData["ContactSuccess"] = targetStatus == "PROCESSING"
            ? "Đã tiếp nhận liên hệ."
            : "Đã đánh dấu liên hệ được xử lý.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        [FromRoute] long id,
        [FromForm] string? expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid
            || expectedStatus is not ("NEW" or "PROCESSING" or "CLOSED"))
        {
            return BadRequest("Thông tin xóa liên hệ không hợp lệ.");
        }

        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(claim, out var actorId))
        {
            return Forbid();
        }

        var deletedRows = await _context.Set<ContactMessage>()
            .Where(message =>
                message.Id == id
                && message.Status == expectedStatus
                && _context.Set<DoraFood.Models.User>().Any(user =>
                    user.Id == actorId && user.IsActive))
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedRows == 0)
        {
            TempData["ContactError"] =
                "Liên hệ đã thay đổi, đã bị xóa hoặc phiên làm việc không còn hợp lệ. "
                + "Anh hãy kiểm tra lại danh sách.";
        }
        else
        {
            TempData["ContactSuccess"] = "Đã xóa liên hệ.";
        }

        return RedirectToAction(nameof(Index));
    }
}
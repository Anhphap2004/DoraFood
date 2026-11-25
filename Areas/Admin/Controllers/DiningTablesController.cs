using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[AutoValidateAntiforgeryToken]
[Route("quan-ly/ban")]
public sealed class DiningTablesController(
    DoraFoodContext db, ILogger<DiningTablesController> logger) : Controller
{
    private const string LockSql = """
        DECLARE @LockResult int;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = N'DoraFood.Reservations',
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 10000;
        IF @LockResult < 0
        THROW 56030, N'Không thể khóa dữ liệu đặt bàn.', 1;
        """;

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] long? areaId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || areaId is <= 0)
            return BadRequest("Khu vực không hợp lệ.");

        var areas = await LoadAreasAsync(cancellationToken);
        if (areaId.HasValue && !areas.Any(area => area.Id == areaId.Value))
            return NotFound("Không tìm thấy khu vực.");

        var tables = await TableRows().AsNoTracking()
            .Where(table => !areaId.HasValue || table.AreaId == areaId)
            .OrderBy(table => table.AreaName).ThenBy(table => table.AreaId).ThenBy(table => table.Code)
            .ToListAsync(cancellationToken);

        return View(new DiningTableAdminIndex(areas, tables, areaId));
    }

    [HttpGet("them")]
    public async Task<IActionResult> Create([FromQuery] long? areaId, CancellationToken cancellationToken)
    {
        var areas = await LoadAreasAsync(cancellationToken);
        return View("Form", new DiningTableAdminForm(null,
            new DiningTableAdminInput { AreaId = areaId, Capacity = 4 }, areas));
    }

    [HttpPost("them")]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] DiningTableAdminInput input,
        CancellationToken cancellationToken) => SaveAsync(null, input, cancellationToken);

    [HttpGet("{id:long}/sua")]
    public async Task<IActionResult> Edit([FromRoute] long id, CancellationToken cancellationToken)
    {
        var input = await db.Set<DiningTable>().AsNoTracking().Where(table => table.Id == id)
            .Select(table => new DiningTableAdminInput
            {
                Code = table.Code,
                AreaId = table.AreaId,
                Capacity = table.Capacity,
                Note = table.Note,
                IsActive = table.IsActive
            }).SingleOrDefaultAsync(cancellationToken);

        return input is null ? NotFound() : await FormAsync(id, input, cancellationToken);
    }

    [HttpPost("{id:long}/sua")]
    public Task<IActionResult> Edit([FromRoute] long id, [Bind(Prefix = "Input")] DiningTableAdminInput input,
        CancellationToken cancellationToken) => SaveAsync(id, input, cancellationToken);

    [HttpPost("{id:long}/xoa")]
    public async Task<IActionResult> Delete([FromRoute] long id,
        [FromForm] long? returnAreaId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id <= 0)
            return BadRequest("Bàn cần xóa không hợp lệ.");

        IActionResult Back() => RedirectToAction(nameof(Index), new { areaId = returnAreaId });

        return await WriteAsync(async () =>
        {
            var state = await TableRows().AsNoTracking()
                .SingleOrDefaultAsync(table => table.Id == id, cancellationToken);
            if (state is null)
                return NotFound();

            if (state.InUse)
            {
                TempData["DiningTableError"] = "Bàn đang phục vụ hoặc đã được gán cho lịch đặt còn hiệu lực.";
                return Back();
            }

            var table = await db.Set<DiningTable>().AsTracking()
                .SingleAsync(item => item.Id == id, cancellationToken);

            if (state.HasHistory)
            {
                table.IsActive = false;
                db.Entry(table).Property(item => item.IsActive).IsModified = true;
            }
            else
            {
                db.Set<DiningTable>().Remove(table);
            }

            await db.SaveChangesAsync(cancellationToken);
            TempData["DiningTableSuccess"] = state.HasHistory
                ? $"Đã ngừng sử dụng bàn {table.Code} và giữ lịch sử."
                : $"Đã xóa bàn {table.Code}.";

            return Back();
        }, () =>
        {
            TempData["DiningTableError"] = "Chưa thể xóa hoặc ngừng sử dụng bàn. Hãy tải lại và thử lại.";
            return Task.FromResult(Back());
        }, cancellationToken);
    }

    private async Task<IActionResult> SaveAsync(long? id, DiningTableAdminInput input,
        CancellationToken cancellationToken)
    {
        var code = input.Code?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9_-]{0,29}$"))
            ModelState.AddModelError("Input.Code", "Dùng 1–30 ký tự gồm chữ không dấu, số, dấu gạch ngang hoặc gạch dưới.");

        if (!ModelState.IsValid)
            return await FormAsync(id, input, cancellationToken);

        return await WriteAsync(async () =>
        {
            var area = await db.Set<DiningArea>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == input.AreaId, cancellationToken);
            if (area is null || (!area.IsActive && (!id.HasValue || input.IsActive)))
            {
                ModelState.AddModelError("Input.AreaId", "Hãy chọn một khu vực đang hoạt động.");
                return await FormAsync(id, input, cancellationToken);
            }

            var table = new DiningTable();
            if (id is long tableId)
            {
                var existing = await db.Set<DiningTable>().AsTracking()
                    .SingleOrDefaultAsync(item => item.Id == tableId, cancellationToken);
                if (existing is null)
                    return NotFound();

                table = existing;
                var changingSetup = table.AreaId != input.AreaId || table.Code != code
                    || table.Capacity != input.Capacity || table.IsActive != input.IsActive;
                if (changingSetup && await TableRows().AnyAsync(row => row.Id == tableId
                        && (row.HasOpenOrder || row.HasCurrentBooking), cancellationToken))
                {
                    ModelState.AddModelError("", "Bàn đang có đơn hoặc lịch đặt còn hiệu lực. Hiện chỉ có thể sửa ghi chú.");
                    return await FormAsync(id, input, cancellationToken);
                }
            }

            if (await db.Set<DiningTable>().AnyAsync(item => item.Code.ToUpper() == code
                    && (!id.HasValue || item.Id != id), cancellationToken))
            {
                ModelState.AddModelError("Input.Code", "Mã bàn đã tồn tại.");
                return await FormAsync(id, input, cancellationToken);
            }

            table.AreaId = area.Id;
            table.Code = code;
            table.Capacity = input.Capacity!.Value;
            table.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

            if (id.HasValue)
            {
                table.IsActive = input.IsActive;
                db.Entry(table).State = EntityState.Modified;
            }
            else
            {
                table.IsActive = true;
                db.Set<DiningTable>().Add(table);
            }

            await db.SaveChangesAsync(cancellationToken);
            TempData["DiningTableSuccess"] = id.HasValue
                ? $"Đã lưu bàn {table.Code}."
                : $"Đã thêm bàn {table.Code}.";

            return RedirectToAction(nameof(Index), new { areaId = area.Id });
        }, async () =>
        {
            ModelState.AddModelError("", "Chưa lưu được bàn. Hãy kiểm tra lại thông tin và thử lại.");
            return await FormAsync(id, input, cancellationToken);
        }, cancellationToken);
    }

    private IQueryable<DiningTableAdminRow> TableRows()
    {
        var now = DateTime.UtcNow;
        return db.Set<DiningTable>().Select(table => new DiningTableAdminRow
        {
            Id = table.Id,
            AreaId = table.AreaId,
            AreaName = table.Area.Name,
            Code = table.Code,
            Capacity = table.Capacity,
            IsActive = table.IsActive,
            AreaIsActive = table.Area.IsActive,
            Note = table.Note,
            HasOpenOrder = table.OrderTables.Any(link => link.ReleasedAtUtc == null),
            HasCurrentBooking = table.Reservations.Any(booking => booking.Status == "SEATED"
                || ((booking.Status == "PENDING" || booking.Status == "CONFIRMED") && booking.EndsAtUtc > now)),
            HasHistory = table.OrderTables.Any() || table.Reservations.Any()
        });
    }

    private async Task<IActionResult> FormAsync(long? id, DiningTableAdminInput input,
        CancellationToken cancellationToken) =>
        View("Form", new DiningTableAdminForm(id, input, await LoadAreasAsync(cancellationToken)));

    private Task<List<DiningAreaOption>> LoadAreasAsync(CancellationToken cancellationToken) =>
        db.Set<DiningArea>().AsNoTracking().OrderBy(area => area.Name).ThenBy(area => area.Code)
            .Select(area => new DiningAreaOption(area.Id, area.Name, area.Code, area.IsActive))
            .ToListAsync(cancellationToken);

    private async Task<IActionResult> WriteAsync(Func<Task<IActionResult>> write,
        Func<Task<IActionResult>> onError, CancellationToken cancellationToken)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(LockSql, cancellationToken);
                var result = await write();
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (Exception error) when (error is DbUpdateException or SqlException)
        {
            TempData.Remove("DiningTableSuccess");
            logger.LogWarning(error, "Không ghi được thay đổi bàn.");
            return await onError();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.Filters;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[RequirePermission("reports.view")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/hoa-don")]
public sealed class InvoiceHistoryController : Controller
{
    private readonly DoraFoodContext _db;

    public InvoiceHistoryController(DoraFoodContext db) => _db = db;

    [HttpGet("", Name = "DoraFoodInvoiceHistory")]
    public async Task<IActionResult> Index(string? keyword, string? type,
        int page = 1, CancellationToken cancellationToken = default)
    {
        keyword = keyword?.Trim();
        type = string.IsNullOrEmpty(type) ? null : type;

        if (!ModelState.IsValid || keyword?.Length > 100
            || type is not (null or "DINE_IN" or "DELIVERY" or "TAKEAWAY"))
            return BadRequest("Bộ lọc hóa đơn không hợp lệ.");

        const int pageSize = 20;
        var query = HistoricalInvoices();

        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(invoice =>
                invoice.Number.Contains(keyword) || invoice.Order.Code.Contains(keyword));

        if (type is not null)
            query = query.Where(invoice => invoice.Order.OrderType == type);

        var count = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        page = Math.Clamp(page, 1, pages);

        var items = await query.OrderByDescending(invoice => invoice.IssuedAtUtc)
            .ThenByDescending(invoice => invoice.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(invoice => new InvoiceRow(invoice.Id, invoice.Number, invoice.Order.Code,
                invoice.Order.OrderType, invoice.Status, invoice.GrandTotal ?? 0m,
                invoice.IssuedByNavigation.UserName, invoice.IssuedAtUtc))
            .ToListAsync(cancellationToken);

        return View(new InvoicePage(items, keyword, type, page, pages, count));
    }

    [HttpGet("{id:long}", Name = "DoraFoodInvoiceHistoryDetails")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var invoice = await HistoricalInvoices()
            .Include(item => item.Order).ThenInclude(order => order.OrderTables).ThenInclude(link => link.Table)
            .Include(item => item.IssuedByNavigation).Include(item => item.InvoiceLines)
            .AsSplitQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (invoice is null)
            return NotFound();

        var orderKey = invoice.OrderId.ToString(CultureInfo.InvariantCulture);
        var operations = await _db.Set<AuditLog>().AsNoTracking()
            .Where(log => log.EntityKey == orderKey
                && ((log.EntityName == "Orders" && log.Action.StartsWith("staff.order."))
                    || (log.EntityName == "Order" && log.Action == "TABLE_CASH_CHECKOUT")))
            .OrderByDescending(log => log.Id).Take(100)
            .Select(log => new Activity(log.Actor == null ? "—" : log.Actor.UserName,
                log.Summary ?? "", log.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var issuedAtUtc = invoice.IssuedAtUtc!.Value;
        var paid = await _db.Set<Payment>().AsNoTracking()
            .Where(payment => payment.OrderId == invoice.OrderId
                && payment.Status == "SUCCEEDED"
                && payment.PaidAtUtc.HasValue
                && payment.PaidAtUtc.Value <= issuedAtUtc)
            .GroupBy(payment => payment.Purpose)
            .Select(group => new { Purpose = group.Key, Amount = group.Sum(payment => payment.Amount) })
            .ToListAsync(cancellationToken);

        var refunded = await _db.Set<Refund>().AsNoTracking()
            .Where(refund => refund.Payment.OrderId == invoice.OrderId
                && refund.Status == "SUCCEEDED"
                && refund.RefundedAtUtc.HasValue
                && refund.RefundedAtUtc.Value <= issuedAtUtc)
            .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;

        var deposit = paid.Where(item => item.Purpose == "DEPOSIT").Sum(item => item.Amount);
        var settlement = paid.Where(item => item.Purpose == "SETTLEMENT").Sum(item => item.Amount);

        return View(new InvoiceDetails(invoice, operations, deposit, settlement, refunded));
    }

    private IQueryable<Invoice> HistoricalInvoices() => _db.Set<Invoice>()
        .AsNoTracking().Where(invoice => invoice.IssuedAtUtc.HasValue
            && (invoice.Status == "ISSUED" || invoice.Status == "VOID"));

    public static string OrderTypeName(string type) => type switch
    {
        "DINE_IN" => "Tại bàn",
        "DELIVERY" => "Giao hàng",
        "TAKEAWAY" => "Mang đi",
        _ => type
    };

    public sealed record InvoiceRow(long Id, string Number, string OrderCode, string Type,
        string Status, decimal Total, string IssuedBy, DateTime? IssuedAtUtc);

    public sealed record InvoicePage(IReadOnlyList<InvoiceRow> Items, string? Keyword,
        string? Type, int Page, int Pages, int Count);

    public sealed record Activity(string Actor, string Summary, DateTime AtUtc);

    public sealed record InvoiceDetails(Invoice Invoice, IReadOnlyList<Activity> Operations,
        decimal Deposit, decimal Settlement, decimal Refunded);
}

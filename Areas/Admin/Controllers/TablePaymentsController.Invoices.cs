using System.Linq;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public partial class TablePaymentsController
{
    [HttpGet(
        "/quan-ly/don-goi-mon/{orderId:long}/hoa-don",
        Name = "DoraFoodTableInvoice")]
    public async Task<IActionResult> TableInvoice(
        [FromRoute] long orderId)
    {
        if (!ModelState.IsValid || orderId <= 0)
        {
            return BadRequest(
                "Thông tin đơn hàng không hợp lệ.");
        }

        var cancellationToken =
            HttpContext.RequestAborted;

        // Chỉ đọc hóa đơn đã xuất của đơn tại bàn.
        var invoice = await _context.Set<Invoice>()
            .AsNoTracking()
            .Include(item => item.InvoiceLines)
            .Include(item => item.IssuedByNavigation)
            .Include(item => item.Order)
                .ThenInclude(order => order.OrderTables)
                .ThenInclude(link => link.Table)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                item =>
                    item.OrderId == orderId
                    && item.Order.OrderType == "DINE_IN"
                    && item.Status == "ISSUED"
                    && item.IssuedAtUtc.HasValue,
                cancellationToken);

        if (invoice == null)
        {
            return NotFound(
                "Đơn chưa có hóa đơn đã xuất.");
        }

        var issuedAtUtc = invoice.IssuedAtUtc!.Value;

        // Chỉ lấy khoản thu thành công trước hoặc đúng
        // thời điểm xuất hóa đơn.
        var payments = await _context.Set<Payment>()
            .AsNoTracking()
            .Where(payment =>
                payment.OrderId == orderId
                && payment.Status == "SUCCEEDED"
                && payment.PaidAtUtc.HasValue
                && payment.PaidAtUtc.Value <= issuedAtUtc)
            .Select(payment => new
            {
                payment.Purpose,
                payment.Amount
            })
            .ToListAsync(cancellationToken);

        // Khoản hoàn tiền đã thành công tính đến lúc xuất bill.
        var refundedAmount = await _context.Set<Refund>()
            .AsNoTracking()
            .Where(refund =>
                refund.Payment.OrderId == orderId
                && refund.Status == "SUCCEEDED"
                && refund.RefundedAtUtc.HasValue
                && refund.RefundedAtUtc.Value <= issuedAtUtc)
            .SumAsync(
                refund => (decimal?)refund.Amount,
                cancellationToken)
            ?? 0m;

        var model = new TableInvoiceViewModel
        {
            Invoice = invoice,

            OrderCode = invoice.Order.Code,

            CashierName =
                invoice.IssuedByNavigation.UserName,

            // Giữ thông tin bàn đã phục vụ,
            // kể cả khi bàn đã được giải phóng.
            TableCodes = invoice.Order.OrderTables
                .Select(link => link.Table.Code)
                .Distinct()
                .OrderBy(code => code)
                .ToList(),

            DepositAmount = payments
                .Where(payment =>
                    payment.Purpose == "DEPOSIT")
                .Sum(payment => payment.Amount),

            SettlementAmount = payments
                .Where(payment =>
                    payment.Purpose == "SETTLEMENT")
                .Sum(payment => payment.Amount),

            RefundedAmount = refundedAmount
        };

        ViewData["Title"] =
            $"Hóa đơn {invoice.Number}";

        return View("TableInvoice", model);
    }
}
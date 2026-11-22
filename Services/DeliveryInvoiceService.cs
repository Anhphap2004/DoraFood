using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Services;

public static class DeliveryInvoiceService
{
    public static IQueryable<Invoice> Issued(DoraFoodContext db)
    {
        return db.Set<Invoice>()
            .AsNoTracking()
            .Where(invoice =>
                invoice.Status == "ISSUED"
                && invoice.Order.OrderType == "DELIVERY"
                && invoice.Order.Status == "COMPLETED");
    }

    // Gọi trong transaction của thao tác hoàn tất đơn hoặc tạo hóa đơn cũ.
    // Trả về null khi thành công, hoặc nội dung lỗi để caller hoàn tác.
    public static async Task<string?> IssueAsync(
        DoraFoodContext db,
        long orderId,
        long issuedBy,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var order = await db.Set<Order>()
            .AsNoTracking()
            .Where(item => item.Id == orderId
                && item.OrderType == "DELIVERY"
                && item.Status == "COMPLETED")
            .Select(item => new
            {
                item.GuestName,
                item.DeliveryAddress
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
            return "Chỉ tạo hóa đơn cho đơn giao hàng đã hoàn tất.";

        var invoiceStatus = await db.Set<Invoice>()
            .Where(invoice => invoice.OrderId == orderId)
            .Select(invoice => invoice.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (invoiceStatus == "ISSUED")
            return null;

        if (invoiceStatus is not null)
            return "Đơn đã có bản ghi hóa đơn. Hãy kiểm tra hóa đơn hiện tại.";

        var paidAmount = await db.Set<Payment>()
            .Where(payment =>
                payment.OrderId == orderId
                && payment.Purpose == "SETTLEMENT"
                && payment.Status == "SUCCEEDED"
                && payment.PaidAtUtc.HasValue)
            .SumAsync(payment => (decimal?)payment.Amount, cancellationToken)
            ?? 0m;

        var items = await db.Set<OrderItem>()
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.Status != "CANCELLED")
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.NameSnapshot,
                item.Quantity,
                item.UnitPrice
            })
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
            return "Đơn giao hàng chưa có món hợp lệ để lập hóa đơn.";

        var lines = items.Select(item => new
        {
            Item = item,
            Subtotal = Money(item.Quantity * item.UnitPrice)
        }).ToList();

        var subtotal = lines.Sum(line => line.Subtotal);

        var discount = await db.Set<OrderDiscount>()
            .Where(item => item.OrderId == orderId)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken)
            ?? 0m;

        if (paidAmount <= 0m
            || subtotal <= 0m
            || discount < 0m
            || discount > subtotal
            || subtotal - discount != paidAmount)
        {
            return "Tiền món sau giảm giá chưa khớp khoản đã thu. Hãy kiểm tra chi tiết đơn trước khi tạo hóa đơn.";
        }

        var invoice = new Invoice
        {
            OrderId = orderId,
            Number = $"HD-WEB-{orderId}",
            Status = "DRAFT",
            CustomerName = order.GuestName,
            CustomerAddress = order.DeliveryAddress,
            Subtotal = subtotal,
            DiscountAmount = discount,
            TaxAmount = 0m,
            IssuedBy = issuedBy
        };

        decimal cumulativeSubtotal = 0m;
        decimal allocatedDiscount = 0m;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            cumulativeSubtotal += line.Subtotal;

            // Dùng tổng lũy kế để tổng giảm giá các dòng khớp hóa đơn.
            var cumulativeDiscount = index == lines.Count - 1
                ? discount
                : Money(discount * (cumulativeSubtotal / subtotal));

            invoice.InvoiceLines.Add(new InvoiceLine
            {
                LineNumber = index + 1,
                LineType = "DISH",
                OrderItemId = line.Item.Id,
                Description = line.Item.NameSnapshot,
                Unit = "Đơn vị",
                Quantity = line.Item.Quantity,
                UnitPrice = line.Item.UnitPrice,
                DiscountAmount = cumulativeDiscount - allocatedDiscount,
                TaxRate = 0m,
                TaxAmount = 0m
            });

            allocatedDiscount = cumulativeDiscount;
        }

        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        // SQL Server yêu cầu lưu dòng chi tiết khi hóa đơn còn là DRAFT.
        invoice.Status = "ISSUED";
        invoice.IssuedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);

        return null;
    }

    private static decimal Money(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
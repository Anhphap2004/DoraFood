using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public sealed partial class StaffController
{
    [HttpPost("don/{id:long}/mon/{lineId:long}/len-mon", Name = "DoraFoodStaffServeDish")]
    public Task<IActionResult> ServeDish([FromRoute] long id, [FromRoute] long lineId,
        [FromForm] bool fromBoard, [FromForm] bool fromAdminBoard, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id <= 0 || lineId <= 0)
            return Task.FromResult<IActionResult>(BadRequest("Thông tin món không hợp lệ."));

        IActionResult Back()
        {
            if (!fromBoard)
                return RedirectToRoute(TableOrderRoute, new { id });

            return RedirectToRoute(fromAdminBoard && ViewData["CanCollectTablePayments"] is true
                ? "DoraFoodAdminTableService" : TableBoardRoute);
        }

        return WriteAtTableAsync(async () =>
        {
            var lines = _db.Set<OrderItem>().Where(line => line.Id == lineId && line.OrderId == id);
            var line = await lines.Where(item => OpenTableOrders().Any(order => order.Id == item.OrderId))
                .Select(item => new { item.NameSnapshot, item.Status }).SingleOrDefaultAsync(cancellationToken);

            if (line is null || line.Status == "CANCELLED")
            {
                TempData["StaffError"] = "Bàn chưa nhận khách, đơn đã đóng hoặc món đã hủy.";
                return fromBoard ? Back() : RedirectToRoute(TableBoardRoute);
            }

            if (line.Status == "SERVED")
                return Back();

            var changed = await lines
                .Where(item => item.Status == "NEW" || item.Status == "PREPARING" || item.Status == "READY")
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, "SERVED"), cancellationToken);
            if (changed != 1)
                throw new DbUpdateException($"Không lưu được trạng thái phục vụ của dòng món {lineId}.");

            Audit(id, "staff.order.serve-item", $"Đã lên món {line.NameSnapshot} (dòng {lineId}).");
            TempData["StaffSuccess"] = $"Đã lên món {line.NameSnapshot}.";
            return Back();
        }, cancellationToken);
    }
}

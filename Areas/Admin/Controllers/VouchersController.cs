using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Security.Claims;
using DoraFood.Services;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class VouchersController : Controller
{
    private readonly DoraFoodContext _context;
    private readonly VoucherImageService _imageService;

    public VouchersController(
        DoraFoodContext context,
        VoucherImageService imageService)
    {
        _context = context;
        _imageService = imageService;
    }

    // =====================================================
    // DANH SÁCH
    // GET: /Admin/Vouchers
    // =====================================================
    [HttpGet]
    public async Task<IActionResult> Index(
    int page = 1,
    CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Số trang không hợp lệ.");
        }

        const int pageSize = 20;
        var query = _context.Set<Voucher>()
     .AsNoTracking()
     .Include(voucher => voucher.Cover);

        var totalItems = await query.CountAsync(cancellationToken);

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalItems / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var vouchers = await query
            .OrderByDescending(voucher => voucher.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var voucherIds = vouchers
            .Select(voucher => voucher.Id)
            .ToArray();

        // Một voucher trên cùng một đơn chỉ tính một lượt.
        // Đơn đã hủy không chiếm lượt sử dụng.
        var usageCounts = await _context.Set<OrderDiscount>()
            .AsNoTracking()
            .Where(discount =>
                discount.VoucherId.HasValue
                && voucherIds.Contains(discount.VoucherId.Value)
                && discount.Order.Status != "CANCELLED")
            .Select(discount => new
            {
                VoucherId = discount.VoucherId!.Value,
                discount.OrderId
            })
            .Distinct()
            .GroupBy(discount => discount.VoucherId)
            .Select(group => new
            {
                VoucherId = group.Key,
                UsedCount = group.LongCount()
            })
            .ToDictionaryAsync(
                item => item.VoucherId,
                item => item.UsedCount,
                cancellationToken);

        ViewData["Page"] = page;
        ViewData["TotalPages"] = totalPages;
        ViewData["TotalItems"] = totalItems;
        ViewData["VoucherUsageCounts"] = usageCounts;

        return View(vouchers);
    }

    // =====================================================
    // THÊM MÃ GIẢM GIÁ
    // =====================================================

    [HttpGet]
    public IActionResult Create()
    {
        var localNow = DateTime.UtcNow.AddHours(7);

        return View(new VoucherInput
        {
            DiscountType = "PERCENT",
            MinimumOrder = 0m,
            StartsAtLocal = localNow,
            EndsAtLocal = localNow.AddDays(30),
            IsActive = true
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(
     VoucherInput input,
     CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var uploaderId = await GetUploaderIdAsync(cancellationToken);

        if (!uploaderId.HasValue)
        {
            return Forbid();
        }

        var voucher = new Voucher();

        ApplyInput(voucher, input);

        _context.Set<Voucher>().Add(voucher);

        if (!await SaveWithImageAsync(
            voucher,
            input,
            uploaderId.Value,
            cancellationToken))
        {
            return View(input);
        }

        TempData["VoucherSuccess"] =
            $"Đã tạo mã giảm giá {voucher.Code}.";

        return RedirectToAction(nameof(Index));
    }

    // =====================================================
    // SỬA MÃ GIẢM GIÁ
    // =====================================================

    [HttpGet]
    public async Task<IActionResult> Edit(
    long id,
    CancellationToken cancellationToken = default)
    {
        var voucher = await _context.Set<Voucher>()
            .AsNoTracking()
            .Include(item => item.Cover)
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (voucher is null)
        {
            return NotFound("Không tìm thấy mã giảm giá.");
        }

        ViewData["VoucherId"] = voucher.Id;

        var input = ToInput(voucher);

        input.CurrentCoverUrl = voucher.Cover?.Url;

        return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(
        long id,
        VoucherInput input,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.Set<Voucher>()
            .Include(item => item.Cover)
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (voucher is null)
        {
            return NotFound("Không tìm thấy mã giảm giá.");
        }

        ViewData["VoucherId"] = voucher.Id;

        // Lấy lại ảnh hiện tại từ database khi form có lỗi.
        input.CurrentCoverUrl = voucher.Cover?.Url;

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var uploaderId = await GetUploaderIdAsync(cancellationToken);

        if (!uploaderId.HasValue)
        {
            return Forbid();
        }

        ApplyInput(voucher, input);

        if (!await SaveWithImageAsync(
            voucher,
            input,
            uploaderId.Value,
            cancellationToken))
        {
            return View(input);
        }

        TempData["VoucherSuccess"] =
            $"Đã cập nhật mã giảm giá {voucher.Code}.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> SaveWithImageAsync(
    Voucher voucher,
    VoucherInput input,
    long uploaderId,
    CancellationToken cancellationToken)
    {
        VoucherImageUpload? upload = null;

        if (input.CoverFile is not null)
        {
            try
            {
                upload = await _imageService.UploadAsync(
                    input.CoverFile,
                    uploaderId,
                    input.Name.Trim(),
                    cancellationToken);
            }
            catch (ValidationException exception)
            {
                ModelState.AddModelError(
                    nameof(VoucherInput.CoverFile),
                    exception.Message);

                return false;
            }
            catch (IOException)
            {
                ModelState.AddModelError(
                    nameof(VoucherInput.CoverFile),
                    "Không lưu được ảnh. Vui lòng thử lại.");

                return false;
            }
            catch (UnauthorizedAccessException)
            {
                ModelState.AddModelError(
                    nameof(VoucherInput.CoverFile),
                    "Ứng dụng chưa có quyền ghi vào thư mục ảnh.");

                return false;
            }

            _context.Set<MediaFile>().Add(upload.Media);

            voucher.Cover = upload.Media;
        }

        // Dùng lại hàm bắt lỗi trùng mã và lỗi cập nhật đang có.
        var saved = await SaveVoucherAsync(cancellationToken);

        if (!saved && upload is not null)
        {
            _imageService.DeleteFile(upload.FullPath);
        }

        return saved;
    }

    private async Task<long?> GetUploaderIdAsync(
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            || userId <= 0)
        {
            return null;
        }

        var allowed = await _context.Set<DoraFood.Models.User>()
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId
                    && user.IsActive
                    && user.Roles.Any(role =>
                        role.Code == "ADMIN"
                        || role.Code == "MANAGER"),
                cancellationToken);

        return allowed ? userId : null;
    }

    // =====================================================
    // BẬT / TẮT
    // Form gửi trạng thái mong muốn, không tự đảo trạng thái.
    // =====================================================

    [HttpPost]
    public async Task<IActionResult> SetActive(
        long id,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id <= 0 || !isActive.HasValue)
        {
            return BadRequest("Thông tin cập nhật không hợp lệ.");
        }

        var active = isActive.Value;

        var changedRows = await _context.Set<Voucher>()
            .Where(voucher => voucher.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    voucher => voucher.IsActive,
                    active),
                cancellationToken);

        if (changedRows == 0)
        {
            return NotFound("Không tìm thấy mã giảm giá.");
        }

        TempData["VoucherSuccess"] = active
            ? "Đã bật mã giảm giá."
            : "Đã tắt mã giảm giá.";

        return RedirectToAction(nameof(Index));
    }

    // =====================================================
    // XÓA
    // Chỉ xóa mã chưa được sử dụng trong đơn hàng.
    // =====================================================

    [HttpPost]
    public async Task<IActionResult> Delete(
        long id,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id <= 0)
        {
            return BadRequest("Mã định danh không hợp lệ.");
        }

        try
        {
            // Kiểm tra điều kiện và xóa trong cùng một câu lệnh SQL.
            var deletedRows = await _context.Set<Voucher>()
                .Where(voucher =>
                    voucher.Id == id
                    && !voucher.OrderDiscounts.Any())
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows == 0)
            {
                var exists = await _context.Set<Voucher>()
                    .AnyAsync(
                        voucher => voucher.Id == id,
                        cancellationToken);

                TempData["VoucherError"] = exists
                    ? "Mã đã được sử dụng trong đơn hàng nên không thể xóa. "
                        + "Anh hãy tắt mã để ngừng sử dụng."
                    : "Mã giảm giá không còn tồn tại.";
            }
            else
            {
                TempData["VoucherSuccess"] =
                    "Đã xóa mã giảm giá.";
            }
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            // Bảo vệ trường hợp dữ liệu liên quan phát sinh đồng thời.
            TempData["VoucherError"] =
                "Mã đang có dữ liệu liên quan nên không thể xóa. "
                + "Anh hãy tắt mã để ngừng sử dụng.";
        }

        return RedirectToAction(nameof(Index));
    }

    // =====================================================
    // CÁC HÀM DÙNG CHUNG
    // =====================================================

    // Đưa dữ liệu từ form vào entity.
    private static void ApplyInput(
        Voucher voucher,
        VoucherInput input)
    {
        voucher.Code = input.Code.Trim().ToUpperInvariant();
        voucher.Name = input.Name.Trim();
        voucher.DiscountType = input.DiscountType;
        voucher.DiscountValue = input.DiscountValue!.Value;

        // Giới hạn giảm tối đa chỉ dùng cho giảm theo phần trăm.
        voucher.MaxDiscount = input.DiscountType == "PERCENT"
            ? input.MaxDiscount
            : null;

        voucher.MinimumOrder = input.MinimumOrder!.Value;
        voucher.UsageLimit = input.UsageLimit;
        voucher.PerCustomerLimit = input.PerCustomerLimit;

        // Form nhập giờ Việt Nam, database lưu UTC.
        voucher.StartsAtUtc = ToUtc(input.StartsAtLocal!.Value);
        voucher.EndsAtUtc = ToUtc(input.EndsAtLocal!.Value);

        voucher.IsActive = input.IsActive;
    }

    // Đưa dữ liệu từ entity lên form sửa.
    private static VoucherInput ToInput(Voucher voucher)
    {
        return new VoucherInput
        {
            Code = voucher.Code,
            Name = voucher.Name,
            DiscountType = voucher.DiscountType,
            DiscountValue = voucher.DiscountValue,
            MaxDiscount = voucher.MaxDiscount,
            MinimumOrder = voucher.MinimumOrder,
            UsageLimit = voucher.UsageLimit,
            PerCustomerLimit = voucher.PerCustomerLimit,

            StartsAtLocal = DateTime.SpecifyKind(
                voucher.StartsAtUtc.AddHours(7),
                DateTimeKind.Unspecified),

            EndsAtLocal = DateTime.SpecifyKind(
                voucher.EndsAtUtc.AddHours(7),
                DateTimeKind.Unspecified),

            IsActive = voucher.IsActive
        };
    }

    private static DateTime ToUtc(DateTime localTime)
    {
        return DateTime.SpecifyKind(
            localTime.AddHours(-7),
            DateTimeKind.Utc);
    }

    // Dùng chung cho thêm và sửa.
    private async Task<bool> SaveVoucherAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(
                "",
                "Dữ liệu vừa thay đổi hoặc đã bị xóa. "
                + "Anh hãy tải lại trang.");

            return false;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627))
        {
            ModelState.AddModelError(
                nameof(VoucherInput.Code),
                "Mã giảm giá đã tồn tại. Vui lòng chọn mã khác.");

            return false;
        }
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoraFood.Areas.Admin.ViewModels;

public class VoucherInput : IValidatableObject
{
    [Display(Name = "Mã giảm giá")]
    [Required(ErrorMessage = "Vui lòng nhập mã giảm giá.")]
    [StringLength(50, ErrorMessage = "Mã giảm giá tối đa 50 ký tự.")]
    [RegularExpression(
        @"^[a-zA-Z0-9_-]+$",
        ErrorMessage =
            "Mã chỉ gồm chữ không dấu, số, dấu gạch ngang hoặc gạch dưới.")]
    public string Code { get; set; } = "";

    [Display(Name = "Tên chương trình")]
    [Required(ErrorMessage = "Vui lòng nhập tên chương trình.")]
    [StringLength(
        150,
        ErrorMessage = "Tên chương trình tối đa 150 ký tự.")]
    public string Name { get; set; } = "";

    [Display(Name = "Hình thức giảm")]
    [Required(ErrorMessage = "Vui lòng chọn hình thức giảm.")]
    [RegularExpression(
        "^(PERCENT|FIXED)$",
        ErrorMessage = "Hình thức giảm không hợp lệ.")]
    public string DiscountType { get; set; } = "PERCENT";

    [Display(Name = "Giá trị giảm")]
    [Required(ErrorMessage = "Vui lòng nhập giá trị giảm.")]
    [Range(
        typeof(decimal),
        "0.01",
        "99999999999999999.99",
        ErrorMessage = "Giá trị giảm phải lớn hơn 0 và nằm trong giới hạn cho phép.")]
    public decimal? DiscountValue { get; set; }

    [Display(Name = "Số tiền giảm tối đa")]
    [Range(
        typeof(decimal),
        "0.01",
        "99999999999999999.99",
        ErrorMessage = "Số tiền giảm tối đa phải lớn hơn 0 và nằm trong giới hạn cho phép.")]
    public decimal? MaxDiscount { get; set; }

    [Display(Name = "Giá trị đơn tối thiểu")]
    [Required(ErrorMessage = "Vui lòng nhập giá trị đơn tối thiểu.")]
    [Range(
        typeof(decimal),
        "0",
        "99999999999999999.99",
        ErrorMessage = "Giá trị đơn tối thiểu phải từ 0 và nằm trong giới hạn cho phép.")]
    public decimal? MinimumOrder { get; set; } = 0m;

    [Display(Name = "Tổng lượt sử dụng")]
    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Tổng lượt sử dụng phải lớn hơn 0.")]
    public int? UsageLimit { get; set; }

    [Display(Name = "Số lượt mỗi khách")]
    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Số lượt mỗi khách phải lớn hơn 0.")]
    public int? PerCustomerLimit { get; set; }

    [Display(Name = "Bắt đầu áp dụng")]
    [Required(ErrorMessage = "Vui lòng chọn thời gian bắt đầu.")]
    public DateTime? StartsAtLocal { get; set; }

    [Display(Name = "Kết thúc áp dụng")]
    [Required(ErrorMessage = "Vui lòng chọn thời gian kết thúc.")]
    public DateTime? EndsAtLocal { get; set; }

    [Display(Name = "Cho phép sử dụng")]
    public bool IsActive { get; set; } = true;

    // File ảnh mới do quản trị viên chọn từ máy.
    [Display(Name = "Ảnh quảng cáo")]
    public IFormFile? CoverFile { get; set; }

    // Controller đọc từ database để hiển thị ảnh hiện tại.
    // Không nhận đường dẫn này từ form gửi lên.
    [BindNever]
    public string? CurrentCoverUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (DiscountType == "PERCENT"
            && DiscountValue.HasValue
            && DiscountValue.Value > 100m)
        {
            yield return new ValidationResult(
                "Mức giảm theo phần trăm không được vượt quá 100%.",
                new[] { nameof(DiscountValue) });
        }

        if (StartsAtLocal.HasValue
            && EndsAtLocal.HasValue
            && EndsAtLocal.Value <= StartsAtLocal.Value)
        {
            yield return new ValidationResult(
                "Thời gian kết thúc phải sau thời gian bắt đầu.",
                new[] { nameof(EndsAtLocal) });
        }

        var amounts = new (string Field, decimal? Value)[]
        {
            (nameof(DiscountValue), DiscountValue),
            (nameof(MaxDiscount), MaxDiscount),
            (nameof(MinimumOrder), MinimumOrder)
        };

        foreach (var amount in amounts)
        {
            if (amount.Value.HasValue
                && decimal.Round(amount.Value.Value, 2)
                    != amount.Value.Value)
            {
                yield return new ValidationResult(
                    "Chỉ được nhập tối đa hai chữ số sau dấu thập phân.",
                    new[] { amount.Field });
            }
        }

        if (CoverFile is not null)
        {
            const long maxSize = 5 * 1024 * 1024;

            if (CoverFile.Length <= 0)
            {
                yield return new ValidationResult(
                    "File ảnh không có dữ liệu.",
                    new[] { nameof(CoverFile) });
            }
            else if (CoverFile.Length > maxSize)
            {
                yield return new ValidationResult(
                    "Ảnh quảng cáo tối đa 5 MB.",
                    new[] { nameof(CoverFile) });
            }
        }
    }
}
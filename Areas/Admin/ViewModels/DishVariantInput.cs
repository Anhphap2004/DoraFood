using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class DishVariantInput
{
    [BindNever]
    public long DishId { get; set; }

    [BindNever]
    public string DishName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập mã khẩu phần.")]
    [StringLength(60, ErrorMessage = "Mã khẩu phần tối đa 60 ký tự.")]
    [RegularExpression(
        @"\A[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*\z",
        ErrorMessage = "Mã chỉ dùng chữ không dấu, số và dấu gạch ngang.")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập tên khẩu phần.")]
    [StringLength(100, ErrorMessage = "Tên khẩu phần tối đa 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập đơn vị bán.")]
    [StringLength(30, ErrorMessage = "Đơn vị bán tối đa 30 ký tự.")]
    public string SellingUnit { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập giá bán.")]
    [Range(typeof(long), "1", "99999999999999999",
        ErrorMessage = "Giá bán phải là số nguyên dương tính bằng đồng.")]
    public long? Price { get; set; }

    public bool IsAvailable { get; set; } = true;
}
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class DishInput
{
    [BindNever]
    public long Id { get; set; }
    [Required(ErrorMessage = "Anh chưa nhập tên món.")]
    [StringLength(200, ErrorMessage = "Tên món tối đa 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập slug.")]
    [StringLength(200, ErrorMessage = "Slug tối đa 200 ký tự.")]
    [RegularExpression(
        @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z",
        ErrorMessage = "Slug chỉ dùng chữ thường không dấu, số và dấu gạch ngang.")]
    public string Slug { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa chọn danh mục món ăn.")]
    public long? CategoryId { get; set; }

    [StringLength(500, ErrorMessage = "Mô tả ngắn tối đa 500 ký tự.")]
    public string? ShortDescription { get; set; }

    [StringLength(500, ErrorMessage = "Lưu ý dị ứng tối đa 500 ký tự.")]
    public string? AllergenNote { get; set; }

    public bool IsFeatured { get; set; }

    public bool IsActive { get; set; } = true;

    [BindNever]
    [ValidateNever]
    public List<SelectListItem> CategoryOptions { get; set; } = new();
}
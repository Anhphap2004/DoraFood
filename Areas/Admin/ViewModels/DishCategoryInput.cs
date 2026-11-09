using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class DishCategoryInput
{
    [BindNever]
    public long Id { get; set; }

    [Required(ErrorMessage = "Anh chưa nhập tên danh mục.")]
    [StringLength(150, ErrorMessage = "Tên danh mục tối đa 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Slug tối đa 200 ký tự.")]
    [RegularExpression(
        @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z",
        ErrorMessage = "Slug chỉ dùng chữ thường không dấu, số và dấu gạch ngang.")]
    public string? Slug { get; set; }

    public long? ParentId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Thứ tự phải từ 0 trở lên.")]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [BindNever]
    [ValidateNever]
    public List<SelectListItem> ParentOptions { get; set; } = new();
}
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class NavigationItemEditInput
{
    [BindNever]
    public long Id { get; set; }

    [Required(ErrorMessage = "Anh chưa nhập tên hiển thị.")]
    [StringLength(100, ErrorMessage = "Tên hiển thị tối đa 100 ký tự.")]
    public string Label { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập đường dẫn.")]
    [StringLength(1000, ErrorMessage = "Đường dẫn tối đa 1000 ký tự.")]
    public string Url { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsVisible { get; set; }
}
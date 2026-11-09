using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class DishImageInput
{
    [BindNever]
    public long DishId { get; set; }

    [BindNever]
    public string DishName { get; set; } = string.Empty;

    [BindNever]
    public string? CurrentImageUrl { get; set; }

    [Required(ErrorMessage = "Anh chưa chọn ảnh.")]
    public IFormFile? Image { get; set; }
}
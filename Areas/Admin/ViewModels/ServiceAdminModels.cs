using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Http;
namespace DoraFood.Areas.Admin.ViewModels;

public sealed class ServiceAdminInput
{
    [BindNever]
    public long Id { get; set; }

    [Required(ErrorMessage = "Anh hãy nhập tên dịch vụ.")]
    [StringLength(150, ErrorMessage = "Tên dịch vụ tối đa 150 ký tự.")]
    [Display(Name = "Tên dịch vụ")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh hãy nhập slug.")]
    [StringLength(200, ErrorMessage = "Slug tối đa 200 ký tự.")]
    [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug chỉ dùng chữ thường không dấu, số và dấu gạch ngang.")]
    [Display(Name = "Slug")]
    public string Slug { get; set; } = string.Empty;

    [Display(Name = "Mô tả")]
    public string? DescriptionHtml { get; set; }

    [Required(ErrorMessage = "Anh hãy nhập giá gốc.")]
    [Display(Name = "Giá gốc (VNĐ)")]
    public decimal? BasePrice { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn ảnh tối đa 500 ký tự.")]
    [Display(Name = "Đường dẫn ảnh")]
    [BindNever]
    public string? ImageUrl { get; set; }

    [Display(Name = "Ảnh dịch vụ")]
    public IFormFile? ImageFile { get; set; }

    [Display(Name = "Hiện trên trang chủ")]
    public bool IsActive { get; set; } = true;
}

public sealed record ServiceAdminRow(
    long Id,
    string Name,
    string Slug,
    decimal BasePrice,
    string? ImageUrl,
    bool IsActive);

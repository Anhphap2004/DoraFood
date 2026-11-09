using System.ComponentModel.DataAnnotations;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class AboutPageInput
{
    [Required(ErrorMessage = "Anh chưa nhập tiêu đề.")]
    [StringLength(200, ErrorMessage = "Tiêu đề tối đa 200 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập nội dung giới thiệu.")]
    public string ContentHtml { get; set; } = string.Empty;

    [StringLength(300, ErrorMessage = "Mô tả SEO tối đa 300 ký tự.")]
    public string? MetaDescription { get; set; }

    public bool IsPublished { get; set; }
}
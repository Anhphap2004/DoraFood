using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class PostFormInput
{
    [BindNever]
    public long Id { get; set; }
    [BindNever]
    public string? CoverUrl { get; set; }

    [Required(ErrorMessage = "Anh hãy nhập tiêu đề.")]
    [StringLength(250, ErrorMessage = "Tiêu đề tối đa 250 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Slug tối đa 250 ký tự.")]
    [RegularExpression(
    @"^[a-z0-9]+(?:-[a-z0-9]+)*$",
    ErrorMessage = "Slug chỉ dùng chữ thường không dấu, số và dấu gạch ngang.")]
    public string? Slug { get; set; }

    [Required(ErrorMessage = "Anh hãy chọn danh mục.")]
    public long? CategoryId { get; set; }

    [StringLength(1000, ErrorMessage = "Tóm tắt tối đa 1.000 ký tự.")]
    public string? Summary { get; set; }

    [Required(ErrorMessage = "Anh hãy nhập nội dung bài viết.")]
    [StringLength(100_000, ErrorMessage = "Nội dung bài viết quá dài.")]
    public string ContentHtml { get; set; } = string.Empty;

    public string? Version { get; set; }

    [BindNever]
    public IEnumerable<SelectListItem> Categories { get; set; }
        = Array.Empty<SelectListItem>();
}
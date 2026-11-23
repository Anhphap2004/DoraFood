using System.ComponentModel.DataAnnotations;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class FaqInput
{
    public long Id { get; set; }

    [Display(Name = "Câu hỏi")]
    [Required(ErrorMessage = "Nhập câu hỏi.")]
    [StringLength(250, ErrorMessage = "Câu hỏi tối đa 250 ký tự.")]
    public string Question { get; set; } = string.Empty;

    [Display(Name = "Câu trả lời")]
    [Required(ErrorMessage = "Nhập câu trả lời.")]
    [StringLength(2000, ErrorMessage = "Câu trả lời tối đa 2.000 ký tự.")]
    public string Answer { get; set; } = string.Empty;

    [Display(Name = "Thứ tự hiển thị")]
    [Range(0, int.MaxValue, ErrorMessage = "Thứ tự phải từ 0 trở lên.")]
    public int SortOrder { get; set; }

    [Display(Name = "Hiển thị cho khách hàng")]
    public bool IsActive { get; set; } = true;

    public string? Version { get; set; }
}
using System;
using System.ComponentModel.DataAnnotations;

namespace DoraFood.ViewModels;

public class DishReviewInput
{
    [Required(ErrorMessage = "Vui lòng chọn số sao.")]
    [Range(1, 5, ErrorMessage = "Điểm đánh giá phải từ 1 đến 5 sao.")]
    public byte? Rating { get; set; }

    [StringLength(2000, ErrorMessage = "Nhận xét tối đa 2000 ký tự.")]
    public string? Body { get; set; }
}

public class DishReviewDisplayViewModel
{
    public string UserName { get; set; } = string.Empty;
    public byte Rating { get; set; }
    public string? Body { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

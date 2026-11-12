using System.ComponentModel.DataAnnotations;

namespace DoraFood.ViewModels;

public class OrderCancellationRequestInput
{
    [Required(ErrorMessage = "Thiếu mã đơn hàng.")]
    [StringLength(40, ErrorMessage = "Mã đơn không được quá 40 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Lý do hủy")]
    [Required(ErrorMessage = "Anh hãy nhập lý do muốn hủy đơn.")]
    [StringLength(500, ErrorMessage = "Lý do hủy tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
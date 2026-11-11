using System.ComponentModel.DataAnnotations;

namespace DoraFood.ViewModels;

public sealed class ChangePasswordInput
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại.")]
    [StringLength(128, ErrorMessage = "Mật khẩu tối đa 128 ký tự.")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [StringLength(
        128,
        MinimumLength = 8,
        ErrorMessage = "Mật khẩu mới cần có từ 8 đến 128 ký tự.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu mới.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận chưa khớp.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
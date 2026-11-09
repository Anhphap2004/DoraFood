using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoraFood.ViewModels;

public sealed class UserAddressInput
{
    [BindNever]
    public long Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên người nhận.")]
    [StringLength(150, ErrorMessage = "Tên người nhận tối đa 150 ký tự.")]
    public string RecipientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [StringLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ giao hàng.")]
    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string AddressLine { get; set; } = string.Empty;
}
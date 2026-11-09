using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoraFood.ViewModels;

public sealed class EditProfileInput
{
    [Required(ErrorMessage = "Anh hãy nhập họ và tên.")]
    [StringLength(150, ErrorMessage = "Họ và tên tối đa 150 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [StringLength(20)]
    public string? Gender { get; set; }

    [BindNever]
    public string? CurrentGender { get; set; }
}
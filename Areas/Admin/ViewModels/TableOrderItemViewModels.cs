using System.ComponentModel.DataAnnotations;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DoraFood.Areas.Admin.ViewModels;

// Form sửa món đã gọi.
public sealed class EditTableOrderItemViewModel
{
    [BindNever]
    public long OrderId { get; set; }

    [BindNever]
    public long ItemId { get; set; }

    [Required(ErrorMessage = "Thiếu phiên bản dữ liệu của món.")]
    [StringLength(64)]
    public string Version { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh hãy nhập số lượng.")]
    [Range(
        typeof(decimal),
        "0.001",
        "999",
        ErrorMessage = "Số lượng phải từ 0,001 đến 999.")]
    public decimal? Quantity { get; set; }

    [StringLength(
        500,
        ErrorMessage = "Ghi chú món tối đa 500 ký tự.")]
    public string? Note { get; set; }

    [Required(ErrorMessage = "Anh hãy nhập lý do điều chỉnh.")]
    [StringLength(
        300,
        ErrorMessage = "Lý do điều chỉnh tối đa 300 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    // Chỉ đọc từ database để hiển thị.
    // Không nhận thông tin món hoặc đơn giá từ trình duyệt.
    [BindNever]
    [ValidateNever]
    public OrderItem Item { get; set; } = null!;

    [BindNever]
    [ValidateNever]
    public string OrderCode { get; set; } = string.Empty;
}

// Form hủy một món trong đơn.
public sealed class CancelTableOrderItemViewModel
{
    [BindNever]
    public long OrderId { get; set; }

    [BindNever]
    public long ItemId { get; set; }

    [Required(ErrorMessage = "Thiếu phiên bản dữ liệu của món.")]
    [StringLength(64)]
    public string Version { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh hãy nhập lý do hủy món.")]
    [StringLength(
        300,
        ErrorMessage = "Lý do hủy món tối đa 300 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    [BindNever]
    [ValidateNever]
    public OrderItem Item { get; set; } = null!;

    [BindNever]
    [ValidateNever]
    public string OrderCode { get; set; } = string.Empty;
}
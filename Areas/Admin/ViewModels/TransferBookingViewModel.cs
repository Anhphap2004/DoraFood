using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class TransferBookingViewModel
{
    [BindNever]
    public long Id { get; set; }

    // Dùng để phát hiện lượt đặt bàn đã được người khác cập nhật.
    [Required(ErrorMessage = "Thiếu phiên bản dữ liệu.")]
    [StringLength(64)]
    public string Version { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh hãy chọn bàn sau khi chuyển.")]
    [MinLength(
        1,
        ErrorMessage = "Phải chọn ít nhất một bàn.")]
    [MaxLength(
        200,
        ErrorMessage = "Chỉ được chọn tối đa 200 bàn.")]
    public List<long> TableIds { get; set; } = new();

    [Required(ErrorMessage = "Anh hãy nhập lý do chuyển bàn.")]
    [StringLength(
        300,
        ErrorMessage = "Lý do chuyển bàn tối đa 300 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    // Các thông tin dưới đây được đọc lại từ database,
    // không nhận thay đổi do trình duyệt gửi lên.
    [BindNever]
    [ValidateNever]
    public Reservation Reservation { get; set; } = null!;

    [BindNever]
    [ValidateNever]
    public string? ActiveOrderCode { get; set; }

    [BindNever]
    [ValidateNever]
    public IReadOnlyList<TransferTableOption> CurrentTables
    {
        get;
        set;
    } = Array.Empty<TransferTableOption>();

    [BindNever]
    [ValidateNever]
    public IReadOnlyList<TransferTableOption> Tables
    {
        get;
        set;
    } = Array.Empty<TransferTableOption>();
}

public sealed class TransferTableOption
{
    public long Id { get; init; }

    public long AreaId { get; init; }

    public string AreaName { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public short Capacity { get; init; }

    // Bàn hiện đang phục vụ lượt khách này.
    public bool IsCurrent { get; init; }

    // Bàn được phép chọn tại thời điểm mở form.
    // Khi lưu, controller vẫn kiểm tra lại.
    public bool IsAvailable { get; init; }

    public string? UnavailableReason { get; init; }
}
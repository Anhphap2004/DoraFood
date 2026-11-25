using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record DiningAreaOption(long Id, string Name, string Code, bool IsActive);

public sealed class DiningTableAdminRow
{
    public long Id { get; init; }
    public long AreaId { get; init; }
    public string AreaName { get; init; } = "";
    public string Code { get; init; } = "";
    public short Capacity { get; init; }
    public bool IsActive { get; init; }
    public bool AreaIsActive { get; init; }
    public string? Note { get; init; }
    public bool HasOpenOrder { get; init; }
    public bool HasCurrentBooking { get; init; }
    public bool HasHistory { get; init; }
    public bool InUse => HasOpenOrder || HasCurrentBooking;
}

public sealed record DiningTableAdminIndex(
    IReadOnlyList<DiningAreaOption> Areas, IReadOnlyList<DiningTableAdminRow> Tables, long? AreaId);

public sealed class DiningTableAdminInput
{
    [Display(Name = "Khu vực")]
    [Required(ErrorMessage = "Hãy chọn khu vực.")]
    public long? AreaId { get; set; }

    [Display(Name = "Mã bàn")]
    [Required(ErrorMessage = "Hãy nhập mã bàn.")]
    [StringLength(30, ErrorMessage = "Mã bàn tối đa 30 ký tự.")]
    public string Code { get; set; } = "";

    [Display(Name = "Sức chứa")]
    [Required(ErrorMessage = "Hãy nhập số chỗ ngồi.")]
    [Range(1, 32767, ErrorMessage = "Số chỗ ngồi phải lớn hơn 0 và tối đa 32767.")]
    public short? Capacity { get; set; }

    [Display(Name = "Cho phép sử dụng")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Ghi chú")]
    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Note { get; set; }
}

public sealed record DiningTableAdminForm(
    long? TableId, DiningTableAdminInput Input, IReadOnlyList<DiningAreaOption> Areas);

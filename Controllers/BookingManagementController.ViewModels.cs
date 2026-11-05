using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DoraFood.Controllers
{
    public partial class BookingManagementController
    {
        public class EditBookingViewModel
        {
            [BindNever]
            public long Id { get; set; }

            [Required(ErrorMessage = "Thiếu phiên bản dữ liệu.")]
            [StringLength(64)]
            public string Version { get; set; } = string.Empty;

            [Required(ErrorMessage = "Vui lòng nhập số khách.")]
            [Range(
                1,
                short.MaxValue,
                ErrorMessage = "Số khách phải từ 1 đến 32767.")]
            public short? PartySize { get; set; }

            [Required(ErrorMessage = "Vui lòng chọn giờ bắt đầu.")]
            [DataType(DataType.DateTime)]
            public DateTime? StartsAtLocal { get; set; }

            // Tự tính cuối buổi phục vụ; không nhận từ form.
            [BindNever]
            [ValidateNever]
            public DateTime? EndsAtLocal { get; set; }

            [MaxLength(
                200,
                ErrorMessage = "Số lượng bàn được chọn vượt giới hạn.")]
            public List<long> TableIds { get; set; } = new();

            [Required(ErrorMessage = "Vui lòng nhập lý do điều chỉnh.")]
            [StringLength(
                300,
                ErrorMessage = "Lý do điều chỉnh tối đa 300 ký tự.")]
            public string Reason { get; set; } = string.Empty;

            [BindNever]
            [ValidateNever]
            public Reservation Reservation { get; set; } = null!;

            [BindNever]
            [ValidateNever]
            public List<DiningTable> Tables { get; set; } = new();
        }

        public class CancelBookingInput
        {
            [Required(ErrorMessage = "Thiếu phiên bản dữ liệu.")]
            [StringLength(64)]
            public string Version { get; set; } = string.Empty;

            [Required(ErrorMessage = "Vui lòng nhập lý do hủy.")]
            [StringLength(
                300,
                ErrorMessage = "Lý do hủy tối đa 300 ký tự.")]
            public string Reason { get; set; } = string.Empty;
        }

        public class BookingDetailsViewModel
        {
            public Reservation Reservation { get; set; } = null!;

            public List<AuditLog> History { get; set; } = new();
        }

        public class BookingManagementViewModel
        {
            public List<Reservation> Reservations { get; set; } = new();

            public string Status { get; set; } = "PENDING";

            public string? Keyword { get; set; }

            public DateTime? ServiceDate { get; set; }

            public long? AreaId { get; set; }

            public List<DiningArea> Areas { get; set; } = new();

            public int CurrentPage { get; set; }

            public int TotalPages { get; set; }

            public int TotalItems { get; set; }
        }

        public class ConfirmBookingViewModel
        {
            // ID lấy từ URL, không nhận thay đổi từ form.
            [BindNever]
            public long Id { get; set; }

            [Required(ErrorMessage =
                "Thiếu phiên bản dữ liệu. Vui lòng tải lại trang.")]
            [StringLength(64)]
            public string Version { get; set; } = string.Empty;

            [MinLength(1,
                ErrorMessage = "Vui lòng chọn ít nhất một bàn.")]
            [MaxLength(200,
                ErrorMessage = "Số lượng bàn được chọn vượt giới hạn.")]
            public List<long> TableIds { get; set; } = new();

            // Thông tin hiển thị do server đọc từ database.
            [BindNever]
            [ValidateNever]
            public Reservation Reservation { get; set; } = null!;

            [BindNever]
            [ValidateNever]
            public List<BookingTableOption> Tables { get; set; } = new();
        }

        public class BookingTableOption
        {
            public long Id { get; set; }

            public string Code { get; set; } = string.Empty;

            public string AreaName { get; set; } = string.Empty;

            public short Capacity { get; set; }

            public bool IsAvailable { get; set; }
        }

        public class TableMapViewModel
        {
            public DateTime ServiceDate { get; set; }

            public string Session { get; set; } = "LUNCH";

            public string SessionLabel { get; set; } = string.Empty;

            // Mốc của buổi để hiển thị; không phải giờ kết thúc dùng bữa.
            public DateTime StartsAtLocal { get; set; }

            public DateTime EndsAtLocal { get; set; }

            public long? AreaId { get; set; }

            public List<DiningArea> Areas { get; set; } = new();

            public List<TableMapItem> Tables { get; set; } = new();
        }

        public class TableMapItem
        {
            public long Id { get; set; }

            public long AreaId { get; set; }

            public string Code { get; set; } = string.Empty;

            public string AreaName { get; set; } = string.Empty;

            public short Capacity { get; set; }

            // Bàn và khu vực đều đang hoạt động.
            public bool IsActive { get; set; }

            // Có khách đang ngồi, độc lập với khoảng thời gian lọc.
            public bool IsOccupiedNow { get; set; }

            // Có yêu cầu còn hiệu lực trong ngày và buổi được chọn.
            public bool HasScheduleConflict { get; set; }

            // Các yêu cầu liên quan để hiển thị trên giao diện.
            public List<Reservation> Reservations { get; set; } = new();
        }
    }
}

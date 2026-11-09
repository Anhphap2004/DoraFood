using System.ComponentModel.DataAnnotations;

namespace DoraFood.ViewModels;

public class CheckoutViewModel
{
    public CartViewModel Cart { get; set; } = new();

    public CheckoutInput Input { get; set; } = new();
    public string? ConfirmationToken { get; set; }
}

public class CheckoutInput
{
    [Required(ErrorMessage = "Anh chưa nhập tên người nhận.")]
    [StringLength(150, ErrorMessage = "Tên người nhận tối đa 150 ký tự.")]
    public string ReceiverName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập số điện thoại.")]
    [StringLength(20)]
    [RegularExpression(
        @"\A\+?[0-9]{9,15}\z",
        ErrorMessage = "Nhập số điện thoại gồm 9–15 chữ số, có thể bắt đầu bằng dấu +.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập địa chỉ giao hàng.")]
    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string DeliveryAddress { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    public string? Note { get; set; }

    [Required(ErrorMessage = "Thiếu phương thức thanh toán.")]
    [RegularExpression(
     @"\ACASH\z",
     ErrorMessage = "Hiện chỉ hỗ trợ tiền mặt khi nhận hàng.")]
    public string PaymentMethod { get; set; } = string.Empty;
}
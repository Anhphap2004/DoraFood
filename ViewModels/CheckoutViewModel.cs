using System.ComponentModel.DataAnnotations;

namespace DoraFood.ViewModels;

public class CheckoutViewModel
{
    public CartViewModel Cart { get; set; } = new();

    public CheckoutInput Input { get; set; } = new();

    public string? ConfirmationToken { get; set; }

    // Mã đã được server kiểm tra và chấp nhận.
    public string? AppliedVoucherCode { get; set; }

    // Số tiền giảm do server tính.
    public decimal DiscountAmount { get; set; }

    // Số tiền khách cần thanh toán sau giảm giá.
    public decimal TotalAmount => Cart.Subtotal - DiscountAmount;
}

public class CheckoutInput
{
    [Required(ErrorMessage = "Anh chưa nhập tên người nhận.")]
    [StringLength(
        150,
        ErrorMessage = "Tên người nhận tối đa 150 ký tự.")]
    public string ReceiverName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập số điện thoại.")]
    [StringLength(
        20,
        ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    [RegularExpression(
        @"\A\+?[0-9]{9,15}\z",
        ErrorMessage =
            "Nhập số điện thoại gồm 9–15 chữ số, có thể bắt đầu bằng dấu +.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Anh chưa nhập địa chỉ giao hàng.")]
    [StringLength(
        500,
        ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string DeliveryAddress { get; set; } = string.Empty;

    [StringLength(
        1000,
        ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    public string? Note { get; set; }

    [Required(ErrorMessage = "Thiếu phương thức thanh toán.")]
    [RegularExpression(
        @"\ACASH\z",
        ErrorMessage = "Hiện chỉ hỗ trợ tiền mặt khi nhận hàng.")]
    public string PaymentMethod { get; set; } = "CASH";

    // Không bắt buộc: khách có thể đặt đơn mà không sử dụng mã.
    [StringLength(
        50,
        ErrorMessage = "Mã giảm giá tối đa 50 ký tự.")]
    [RegularExpression(
        @"\A[a-zA-Z0-9_-]+\z",
        ErrorMessage =
            "Mã giảm giá chỉ gồm chữ không dấu, số, dấu gạch ngang hoặc gạch dưới.")]
    public string? VoucherCode { get; set; }
}
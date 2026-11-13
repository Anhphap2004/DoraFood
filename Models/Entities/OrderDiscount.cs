namespace DoraFood.Models;

public partial class OrderDiscount
{
    public long Id { get; set; }

    public long OrderId { get; set; }

    public long? VoucherId { get; set; }

    public string Description { get; set; } = null!;

    public decimal Amount { get; set; }

    // Để trống khi hệ thống tự áp dụng voucher.
    public long? ApprovedBy { get; set; }

    public virtual User? ApprovedByNavigation { get; set; }

    public virtual Order Order { get; set; } = null!;

    public virtual Voucher? Voucher { get; set; }
}
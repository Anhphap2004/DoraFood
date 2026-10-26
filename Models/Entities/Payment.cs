using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Payment
{
    public long Id { get; set; }

    public long OrderId { get; set; }

    public long MethodId { get; set; }

    public string Purpose { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Status { get; set; } = null!;

    public string? Provider { get; set; }

    public string? ProviderTransactionId { get; set; }

    public Guid IdempotencyKey { get; set; }

    public long? ReceivedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public virtual PaymentMethod Method { get; set; } = null!;

    public virtual Order Order { get; set; } = null!;

    public virtual User? ReceivedByNavigation { get; set; }

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

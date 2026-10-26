using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Refund
{
    public long Id { get; set; }

    public long PaymentId { get; set; }

    public decimal Amount { get; set; }

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = null!;

    public Guid IdempotencyKey { get; set; }

    public long ApprovedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RefundedAtUtc { get; set; }

    public virtual User ApprovedByNavigation { get; set; } = null!;

    public virtual Payment Payment { get; set; } = null!;
}

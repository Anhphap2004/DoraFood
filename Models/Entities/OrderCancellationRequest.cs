using System;

namespace DoraFood.Models;

public partial class OrderCancellationRequest
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long RequestedBy { get; set; }
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = "PENDING";
    public DateTime RequestedAtUtc { get; set; }
    public long? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public byte[] Version { get; set; } = null!;

    public virtual Order Order { get; set; } = null!;
    public virtual User Requester { get; set; } = null!;
    public virtual User? Reviewer { get; set; }
}

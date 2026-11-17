using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class ChatConversation
{
    public long Id { get; set; }

    public long CustomerId { get; set; }

    public long? ReservationId { get; set; }

    public long? AssignedTo { get; set; }

    public string Title { get; set; } = "Tư vấn cùng DoraFood";

    public string Status { get; set; } = "OPEN";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual User Customer { get; set; } = null!;

    public virtual User? AssignedToNavigation { get; set; }

    public virtual Reservation? Reservation { get; set; }

    public virtual ICollection<ChatMessage> Messages { get; set; }
        = new List<ChatMessage>();
}
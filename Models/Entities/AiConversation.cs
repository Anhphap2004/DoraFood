using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class AiConversation
{
    public long Id { get; set; }

    public long? UserId { get; set; }

    public string? Title { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public virtual ICollection<AiMessage> AiMessages { get; set; } = new List<AiMessage>();

    public virtual User? User { get; set; }
}

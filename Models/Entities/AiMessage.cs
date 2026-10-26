using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class AiMessage
{
    public long Id { get; set; }

    public long ConversationId { get; set; }

    public string Role { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? ModelName { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual AiConversation Conversation { get; set; } = null!;
}

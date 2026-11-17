using System;

namespace DoraFood.Models;

public partial class ChatMessage
{
    public long Id { get; set; }

    public long ConversationId { get; set; }

    public long SenderId { get; set; }

    public string SenderType { get; set; } = null!;

    public string Body { get; set; } = null!;

    public Guid ClientMessageId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public virtual ChatConversation Conversation { get; set; }
        = null!;

    public virtual User Sender { get; set; } = null!;
}
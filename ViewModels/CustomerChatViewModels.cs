using System;
using System.ComponentModel.DataAnnotations;

namespace DoraFood.ViewModels;

public sealed class CustomerChatViewModel
{
    public long ConversationId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public long? ReservationId { get; init; }

    public string? ReservationCode { get; init; }

    public bool CanSend => Status == "OPEN";
}

public sealed class SendChatMessageInput
{
    [Required(ErrorMessage = "Anh hãy nhập tin nhắn.")]
    [StringLength(
        2000,
        ErrorMessage = "Tin nhắn tối đa 2.000 ký tự.")]
    public string Body { get; set; } = string.Empty;

    public Guid ClientMessageId { get; set; }
}
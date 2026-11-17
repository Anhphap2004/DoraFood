using System;
using System.Collections.Generic;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed class AdminChatIndexViewModel
{
    public IReadOnlyList<AdminChatRow> Conversations { get; init; }
        = Array.Empty<AdminChatRow>();

    public string? Keyword { get; init; }

    public string Status { get; init; } = "OPEN";

    public bool UnreadOnly { get; init; }

    public int CurrentPage { get; init; }

    public int TotalPages { get; init; }

    public int TotalItems { get; init; }
}

public sealed class AdminChatRow
{
    public long Id { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string? ReservationCode { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? LastMessage { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public int UnreadCount { get; init; }

    public string Preview =>
        string.IsNullOrWhiteSpace(LastMessage)
            ? "Chưa có tin nhắn."
            : LastMessage.Length > 100
                ? LastMessage[..100] + "..."
                : LastMessage;
}

public sealed class AdminChatDetailsViewModel
{
    public long ConversationId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerUserName { get; init; } = string.Empty;

    public string? GuestPhone { get; init; }

    public long? ReservationId { get; init; }

    public string? ReservationCode { get; init; }

    public string Status { get; init; } = string.Empty;

    public bool CanSend => Status == "OPEN";
}
using System;
using System.Collections.Generic;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record ContactMessageRow(
    long Id,
    string FullName,
    string? Email,
    string? PhoneNumber,
    string Subject,
    string Status,
    DateTime CreatedAtUtc);

public sealed record ContactMessagesViewModel(
    IReadOnlyList<ContactMessageRow> Messages,
    string? Keyword,
    string? Status,
    int PageNumber,
    int TotalPages,
    int TotalMessages);
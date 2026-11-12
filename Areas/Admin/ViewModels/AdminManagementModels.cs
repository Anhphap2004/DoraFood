using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record ManagementPager(string Controller, int PageNumber, int TotalPages,
    Dictionary<string, string> Filters);

public sealed record AccountRoleOption(long Id, string Code, string Name);

public sealed record AccountRow(long Id, string UserName, string? Email, bool IsActive,
    DateTime CreatedAtUtc, List<string> Roles);

public sealed record AccountsIndexModel(List<AccountRow> Items, string? Keyword,
    string State, long? RoleId, List<AccountRoleOption> Roles, int Total,
    long ActorId, ManagementPager Pager);

public sealed record AccountDetailsModel(AccountRow Account, string? FullName,
    string? PhoneNumber, string? DateOfBirth, string? Gender, bool EmailConfirmed,
    string Version, long ActorId);

public sealed class AccountEditInput
{
    [BindNever] public long Id { get; set; }
    [BindNever, ValidateNever] public string UserName { get; set; } = string.Empty;
    [Required] public string Version { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long[] RoleIds { get; set; } = Array.Empty<long>();
    [BindNever, ValidateNever] public List<AccountRoleOption> AvailableRoles { get; set; } = new();
}

public sealed class FeedbackRow
{
    public long Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string? Body { get; init; }
    public byte? Rating { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed record FeedbackIndexModel(List<FeedbackRow> Items, string Kind,
    string? Keyword, string? Status, int Total, ManagementPager Pager);

public sealed record FeedbackDetailsModel(FeedbackRow Item, long? ParentId,
    int ReplyCount, string Snapshot);

public sealed record FeedbackSnapshot(long Id, string Kind, string Status, string ContentHash);

public sealed record DailyCashRow(DateTime Day, decimal Received, decimal Refunded)
{
    [NotMapped] public decimal Net => Received - Refunded;
}

public sealed record OverviewOrderRow(string Code, string OrderType, string Status,
    string? GuestName, DateTime OpenedAtUtc);

public sealed record OverviewModel(DateOnly From, DateOnly To, int Orders, int CompletedOrders,
    int CancelledOrders, int Reservations, int ActiveAccounts, int NewContacts,
    int HiddenComments, int HiddenReviews, List<DailyCashRow> Days,
    List<OverviewOrderRow> RecentOrders)
{
    public decimal Received => Days.Sum(day => day.Received);
    public decimal Refunded => Days.Sum(day => day.Refunded);
    public decimal Net => Received - Refunded;
}

public static class ManagementText
{
    public static string FeedbackStatus(string status) => status switch
    {
        "APPROVED" => "Đang hiển thị",
        "REJECTED" => "Đã ẩn",
        "PENDING" => "Chờ hiển thị",
        _ => status
    };

    public static string OrderStatus(string status) => status switch
    {
        "OPEN" => "Chờ tiếp nhận",
        "IN_PROGRESS" => "Đang xử lý",
        "COMPLETED" => "Hoàn tất",
        "CANCELLED" => "Đã hủy",
        _ => status
    };

    public static string OrderType(string type) => type switch
    {
        "DELIVERY" => "Giao hàng",
        "TAKEAWAY" => "Mang về",
        "DINE_IN" => "Tại bàn",
        _ => type
    };
}

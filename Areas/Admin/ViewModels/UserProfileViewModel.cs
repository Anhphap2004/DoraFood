using System;

namespace DoraFood.ViewModels;

public sealed record UserProfileViewModel(
    string UserName,
    string? Email,
    bool EmailConfirmed,
    string? FullName,
    string? PhoneNumber,
    string? DateOfBirth,
    string? Gender,
    string? AvatarUrl,
    DateTime CreatedAtUtc)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(FullName) ? UserName : FullName;
}
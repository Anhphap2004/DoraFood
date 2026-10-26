using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class UserProfile
{
    public long UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? AvatarUrl { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public virtual User User { get; set; } = null!;
}

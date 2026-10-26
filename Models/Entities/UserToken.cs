using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class UserToken
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public byte[] TokenHash { get; set; } = null!;

    public string Purpose { get; set; } = null!;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? ConsumedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual User User { get; set; } = null!;
}

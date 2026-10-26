using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class UserAddress
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public string RecipientName { get; set; } = null!;

    public string PhoneNumber { get; set; } = null!;

    public string AddressLine { get; set; } = null!;

    public bool IsDefault { get; set; }

    public virtual User User { get; set; } = null!;
}

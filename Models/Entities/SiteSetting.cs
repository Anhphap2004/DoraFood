using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class SiteSetting
{
    public string SettingKey { get; set; } = null!;

    public string SettingValue { get; set; } = null!;

    public DateTime UpdatedAtUtc { get; set; }
}

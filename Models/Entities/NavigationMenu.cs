using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class NavigationMenu
{
    public long Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<NavigationItem> NavigationItems { get; set; } = new List<NavigationItem>();
}

using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class NavigationItem
{
    public long Id { get; set; }

    public long MenuId { get; set; }

    public long? ParentId { get; set; }

    public string Label { get; set; } = null!;

    public string Url { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsVisible { get; set; }

    public virtual ICollection<NavigationItem> InverseNavigationItemNavigation { get; set; } = new List<NavigationItem>();

    public virtual ICollection<NavigationItem> InverseParent { get; set; } = new List<NavigationItem>();

    public virtual NavigationMenu Menu { get; set; } = null!;

    public virtual NavigationItem? NavigationItemNavigation { get; set; }

    public virtual NavigationItem? Parent { get; set; }
}

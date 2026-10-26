using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class PostCategory
{
    public long Id { get; set; }

    public long? ParentId { get; set; }

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public bool IsActive { get; set; }

    public virtual ICollection<PostCategory> InverseParent { get; set; } = new List<PostCategory>();

    public virtual PostCategory? Parent { get; set; }

    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();
}

using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Page
{
    public long Id { get; set; }

    public string Title { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string ContentHtml { get; set; } = null!;

    public string? MetaDescription { get; set; }

    public bool IsPublished { get; set; }

    public long? UpdatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public virtual User? UpdatedByNavigation { get; set; }
}

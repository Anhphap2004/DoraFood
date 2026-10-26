using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Banner
{
    public long Id { get; set; }

    public string? Title { get; set; }

    public long MediaFileId { get; set; }

    public string? LinkUrl { get; set; }

    public string Position { get; set; } = null!;

    public int SortOrder { get; set; }

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    public bool IsActive { get; set; }

    public virtual MediaFile MediaFile { get; set; } = null!;
}

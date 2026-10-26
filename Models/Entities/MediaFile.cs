using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class MediaFile
{
    public long Id { get; set; }

    public string Url { get; set; } = null!;

    public string OriginalName { get; set; } = null!;

    public string MimeType { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string? AltText { get; set; }

    public long? UploadedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual ICollection<Banner> Banners { get; set; } = new List<Banner>();

    public virtual ICollection<DishImage> DishImages { get; set; } = new List<DishImage>();

    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();

    public virtual User? UploadedByNavigation { get; set; }
}

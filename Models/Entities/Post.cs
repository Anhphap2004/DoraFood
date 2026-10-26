using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Post
{
    public long Id { get; set; }

    public long CategoryId { get; set; }

    public long AuthorId { get; set; }

    public long? CoverId { get; set; }

    public string Title { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string? Summary { get; set; }

    public string ContentHtml { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? PublishedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual User Author { get; set; } = null!;

    public virtual PostCategory Category { get; set; } = null!;

    public virtual MediaFile? Cover { get; set; }

    public virtual ICollection<PostComment> PostComments { get; set; } = new List<PostComment>();

    public virtual ICollection<Tag> Tags { get; set; } = new List<Tag>();
}

using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class PostComment
{
    public long Id { get; set; }

    public long PostId { get; set; }

    public long UserId { get; set; }

    public long? ParentId { get; set; }

    public string Body { get; set; } = null!;

    public string Status { get; set; } = null!;

    public long? ModeratedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual ICollection<PostComment> InverseParent { get; set; } = new List<PostComment>();

    public virtual ICollection<PostComment> InversePostCommentNavigation { get; set; } = new List<PostComment>();

    public virtual User? ModeratedByNavigation { get; set; }

    public virtual PostComment? Parent { get; set; }

    public virtual Post Post { get; set; } = null!;

    public virtual PostComment? PostCommentNavigation { get; set; }

    public virtual User User { get; set; } = null!;
}

using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class DishReview
{
    public long Id { get; set; }

    public long DishId { get; set; }

    public long UserId { get; set; }

    public byte Rating { get; set; }

    public string? Body { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public virtual Dish Dish { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

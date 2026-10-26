using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Dish
{
    public long Id { get; set; }

    public long CategoryId { get; set; }

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string? ShortDescription { get; set; }

    public string? DescriptionHtml { get; set; }

    public string? AllergenNote { get; set; }

    public bool IsFeatured { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual DishCategory Category { get; set; } = null!;

    public virtual DishImage? DishImage { get; set; }

    public virtual ICollection<DishReview> DishReviews { get; set; } = new List<DishReview>();

    public virtual ICollection<DishVariant> DishVariants { get; set; } = new List<DishVariant>();

    public virtual ICollection<AddOn> AddOns { get; set; } = new List<AddOn>();
}

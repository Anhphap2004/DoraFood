using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class AddOnRecipeItem
{
    public long Id { get; set; }

    public long AddOnId { get; set; }

    public long IngredientId { get; set; }

    public decimal Quantity { get; set; }

    public virtual AddOn AddOn { get; set; } = null!;

    public virtual Ingredient Ingredient { get; set; } = null!;
}

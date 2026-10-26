using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class RecipeItem
{
    public long Id { get; set; }

    public long RecipeId { get; set; }

    public long IngredientId { get; set; }

    public decimal Quantity { get; set; }

    public decimal WastePercent { get; set; }

    public virtual Ingredient Ingredient { get; set; } = null!;

    public virtual Recipe Recipe { get; set; } = null!;
}

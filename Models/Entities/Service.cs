using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Service
{
    public long Id { get; set; }

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string? DescriptionHtml { get; set; }

    public decimal BasePrice { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<ReservationService> ReservationServices { get; set; } = new List<ReservationService>();
}

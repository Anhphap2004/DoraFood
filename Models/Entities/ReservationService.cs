using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class ReservationService
{
    public long Id { get; set; }

    public long ReservationId { get; set; }

    public long ServiceId { get; set; }

    public string NameSnapshot { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public virtual Reservation Reservation { get; set; } = null!;

    public virtual Service Service { get; set; } = null!;
}

using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class DiningTable
{
    public long Id { get; set; }

    public long AreaId { get; set; }

    public string Code { get; set; } = null!;

    public short Capacity { get; set; }

    public bool IsActive { get; set; }

    public string? Note { get; set; }

    public virtual DiningArea Area { get; set; } = null!;

    public virtual OrderTable? OrderTable { get; set; }

    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}

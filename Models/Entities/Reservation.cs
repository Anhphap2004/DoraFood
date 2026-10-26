using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Reservation
{
    public long Id { get; set; }

    public string Code { get; set; } = null!;

    public long? CustomerId { get; set; }

    public string GuestName { get; set; } = null!;

    public string GuestPhone { get; set; } = null!;

    public short PartySize { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public string Status { get; set; } = null!;

    public string? Note { get; set; }

    public long? CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual User? CreatedByNavigation { get; set; }

    public virtual User? Customer { get; set; }

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<ReservationService> ReservationServices { get; set; } = new List<ReservationService>();

    public virtual ICollection<DiningTable> Tables { get; set; } = new List<DiningTable>();
}

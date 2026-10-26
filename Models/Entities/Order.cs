using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class Order
{
    public long Id { get; set; }

    public string Code { get; set; } = null!;

    public long? ReservationId { get; set; }

    public long? CustomerId { get; set; }

    public string OrderType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? GuestName { get; set; }

    public string? GuestPhone { get; set; }

    public string? DeliveryAddress { get; set; }

    public string? Note { get; set; }

    public long CreatedBy { get; set; }

    public DateTime OpenedAtUtc { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual User? Customer { get; set; }

    public virtual Invoice? Invoice { get; set; }

    public virtual ICollection<OrderDiscount> OrderDiscounts { get; set; } = new List<OrderDiscount>();

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    public virtual ICollection<OrderStatusHistory> OrderStatusHistories { get; set; } = new List<OrderStatusHistory>();

    public virtual ICollection<OrderTable> OrderTables { get; set; } = new List<OrderTable>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Reservation? Reservation { get; set; }

    public virtual ICollection<StockDocument> StockDocuments { get; set; } = new List<StockDocument>();
}

using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class User
{
    public long Id { get; set; }

    public string UserName { get; set; } = null!;

    public string NormalizedUserName { get; set; } = null!;

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public string PasswordHash { get; set; } = null!;

    public Guid SecurityStamp { get; set; }

    public bool EmailConfirmed { get; set; }

    public bool IsActive { get; set; }

    public int AccessFailedCount { get; set; }

    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public byte[] Version { get; set; } = null!;

    public virtual ICollection<AiConversation> AiConversations { get; set; } = new List<AiConversation>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<ContactMessage> ContactMessageAssignedToNavigations { get; set; } = new List<ContactMessage>();

    public virtual ICollection<ContactMessage> ContactMessageUsers { get; set; } = new List<ContactMessage>();

    public virtual ICollection<DishReview> DishReviews { get; set; } = new List<DishReview>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<Order> OrderCreatedByNavigations { get; set; } = new List<Order>();

    public virtual ICollection<Order> OrderCustomers { get; set; } = new List<Order>();

    public virtual ICollection<OrderDiscount> OrderDiscounts { get; set; } = new List<OrderDiscount>();

    public virtual ICollection<OrderStatusHistory> OrderStatusHistories { get; set; } = new List<OrderStatusHistory>();

    public virtual ICollection<Page> Pages { get; set; } = new List<Page>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<PostComment> PostCommentModeratedByNavigations { get; set; } = new List<PostComment>();

    public virtual ICollection<PostComment> PostCommentUsers { get; set; } = new List<PostComment>();

    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();

    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();

    public virtual ICollection<Reservation> ReservationCreatedByNavigations { get; set; } = new List<Reservation>();

    public virtual ICollection<Reservation> ReservationCustomers { get; set; } = new List<Reservation>();

    public virtual ICollection<StockCount> StockCounts { get; set; } = new List<StockCount>();

    public virtual ICollection<StockDocument> StockDocumentCreatedByNavigations { get; set; } = new List<StockDocument>();

    public virtual ICollection<StockDocument> StockDocumentPostedByNavigations { get; set; } = new List<StockDocument>();

    public virtual UserAddress? UserAddress { get; set; }

    public virtual UserProfile? UserProfile { get; set; }

    public virtual ICollection<UserToken> UserTokens { get; set; } = new List<UserToken>();

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
}

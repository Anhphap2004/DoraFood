using System;
using System.Collections.Generic;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Data;

public partial class DoraFoodContext : DbContext
{
    public DoraFoodContext(DbContextOptions<DoraFoodContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AddOn> AddOns { get; set; }


    public virtual DbSet<AddOnRecipeItem> AddOnRecipeItems { get; set; }

    public virtual DbSet<AiConversation> AiConversations { get; set; }

    public virtual DbSet<AiMessage> AiMessages { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Banner> Banners { get; set; }

    public virtual DbSet<ContactMessage> ContactMessages { get; set; }

    public virtual DbSet<DiningArea> DiningAreas { get; set; }

    public virtual DbSet<DiningTable> DiningTables { get; set; }

    public virtual DbSet<Dish> Dishes { get; set; }

    public virtual DbSet<DishCategory> DishCategories { get; set; }

    public virtual DbSet<DishImage> DishImages { get; set; }

    public virtual DbSet<DishReview> DishReviews { get; set; }

    public virtual DbSet<DishVariant> DishVariants { get; set; }

    public virtual DbSet<Ingredient> Ingredients { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<InvoiceLine> InvoiceLines { get; set; }

    public virtual DbSet<MediaFile> MediaFiles { get; set; }

    public virtual DbSet<NavigationItem> NavigationItems { get; set; }

    public virtual DbSet<NavigationMenu> NavigationMenus { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<OrderDiscount> OrderDiscounts { get; set; }

    public virtual DbSet<OrderItem> OrderItems { get; set; }

    public virtual DbSet<OrderItemAddOn> OrderItemAddOns { get; set; }

    public virtual DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }

    public virtual DbSet<OrderTable> OrderTables { get; set; }

    public virtual DbSet<Page> Pages { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PaymentMethod> PaymentMethods { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<Post> Posts { get; set; }

    public virtual DbSet<PostCategory> PostCategories { get; set; }

    public virtual DbSet<PostComment> PostComments { get; set; }

    public virtual DbSet<PurchaseOrder> PurchaseOrders { get; set; }

    public virtual DbSet<PurchaseOrderItem> PurchaseOrderItems { get; set; }

    public virtual DbSet<Recipe> Recipes { get; set; }

    public virtual DbSet<RecipeItem> RecipeItems { get; set; }

    public virtual DbSet<Refund> Refunds { get; set; }

    public virtual DbSet<Reservation> Reservations { get; set; }

    public virtual DbSet<ReservationService> ReservationServices { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Service> Services { get; set; }

    public virtual DbSet<SiteSetting> SiteSettings { get; set; }

    public virtual DbSet<StockCount> StockCounts { get; set; }

    public virtual DbSet<StockCountLine> StockCountLines { get; set; }

    public virtual DbSet<StockDocument> StockDocuments { get; set; }

    public virtual DbSet<StockDocumentLine> StockDocumentLines { get; set; }

    public virtual DbSet<StockLot> StockLots { get; set; }

    public virtual DbSet<Supplier> Suppliers { get; set; }

    public virtual DbSet<Tag> Tags { get; set; }

    public virtual DbSet<Unit> Units { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserAddress> UserAddresses { get; set; }

    public virtual DbSet<UserProfile> UserProfiles { get; set; }

    public virtual DbSet<UserToken> UserTokens { get; set; }

    public virtual DbSet<VIngredientInventory> VIngredientInventories { get; set; }

    public virtual DbSet<VInventoryBalance> VInventoryBalances { get; set; }

    public virtual DbSet<VOrderPaymentBalance> VOrderPaymentBalances { get; set; }

    public virtual DbSet<Voucher> Vouchers { get; set; }

    public virtual DbSet<Warehouse> Warehouses { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AddOn>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Price).HasColumnType("decimal(19, 2)");
        });

        modelBuilder.Entity<AddOnRecipeItem>(entity =>
        {
            entity.HasIndex(e => e.AddOnId, "IX_AddOnRecipeItems_AddOnId");

            entity.HasIndex(e => e.IngredientId, "IX_AddOnRecipeItems_IngredientId");

            entity.HasIndex(e => new { e.AddOnId, e.IngredientId }, "UQ_AddOnRecipeItems_1").IsUnique();

            entity.Property(e => e.Quantity).HasColumnType("decimal(19, 4)");

            entity.HasOne(d => d.AddOn).WithMany(p => p.AddOnRecipeItems)
                .HasForeignKey(d => d.AddOnId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AddOnRecipeItems_AddOnId");

            entity.HasOne(d => d.Ingredient).WithMany(p => p.AddOnRecipeItems)
                .HasForeignKey(d => d.IngredientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AddOnRecipeItems_IngredientId");
        });

        modelBuilder.Entity<AiConversation>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_AiConversations_UserId");

            entity.Property(e => e.ClosedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.User).WithMany(p => p.AiConversations)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_AiConversations_UserId");
        });

        modelBuilder.Entity<AiMessage>(entity =>
        {
            entity.HasIndex(e => e.ConversationId, "IX_AiMessages_ConversationId");

            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ModelName)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Role)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.HasOne(d => d.Conversation).WithMany(p => p.AiMessages)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AiMessages_ConversationId");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(e => e.ActorId, "IX_AuditLogs_ActorId");

            entity.Property(e => e.Action)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.EntityKey).HasMaxLength(100);
            entity.Property(e => e.EntityName)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Summary).HasMaxLength(2000);

            entity.HasOne(d => d.Actor).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.ActorId)
                .HasConstraintName("FK_AuditLogs_ActorId");
        });

        modelBuilder.Entity<Banner>(entity =>
        {
            entity.HasIndex(e => e.MediaFileId, "IX_Banners_MediaFileId");

            entity.Property(e => e.EndsAtUtc).HasPrecision(3);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LinkUrl).HasMaxLength(1000);
            entity.Property(e => e.Position)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.StartsAtUtc).HasPrecision(3);
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.MediaFile).WithMany(p => p.Banners)
                .HasForeignKey(d => d.MediaFileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Banners_MediaFileId");
        });

        modelBuilder.Entity<ContactMessage>(entity =>
        {
            entity.HasIndex(e => e.AssignedTo, "IX_ContactMessages_AssignedTo");

            entity.HasIndex(e => e.UserId, "IX_ContactMessages_UserId");

            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("NEW");
            entity.Property(e => e.Subject).HasMaxLength(250);

            entity.HasOne(d => d.AssignedToNavigation).WithMany(p => p.ContactMessageAssignedToNavigations)
                .HasForeignKey(d => d.AssignedTo)
                .HasConstraintName("FK_ContactMessages_AssignedTo");

            entity.HasOne(d => d.User).WithMany(p => p.ContactMessageUsers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_ContactMessages_UserId");
        });

        modelBuilder.Entity<DiningArea>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_DiningAreas_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<DiningTable>(entity =>
        {
            entity.HasIndex(e => e.AreaId, "IX_DiningTables_AreaId");

            entity.HasIndex(e => e.Code, "UQ_DiningTables_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasOne(d => d.Area).WithMany(p => p.DiningTables)
                .HasForeignKey(d => d.AreaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DiningTables_AreaId");
        });

        modelBuilder.Entity<Dish>(entity =>
        {
            entity.HasIndex(e => e.CategoryId, "IX_Dishes_CategoryId");

            entity.HasIndex(e => e.Slug, "UQ_Dishes_1").IsUnique();

            entity.Property(e => e.AllergenNote).HasMaxLength(500);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.ShortDescription).HasMaxLength(500);
            entity.Property(e => e.Slug)
                .HasMaxLength(200)
                .IsUnicode(false);

            entity.HasOne(d => d.Category).WithMany(p => p.Dishes)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Dishes_CategoryId");

            entity.HasMany(d => d.AddOns).WithMany(p => p.Dishes)
                .UsingEntity<Dictionary<string, object>>(
                    "DishAddOn",
                    r => r.HasOne<AddOn>().WithMany()
                        .HasForeignKey("AddOnId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_DishAddOns_AddOnId"),
                    l => l.HasOne<Dish>().WithMany()
                        .HasForeignKey("DishId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_DishAddOns_DishId"),
                    j =>
                    {
                        j.HasKey("DishId", "AddOnId");
                        j.ToTable("DishAddOns");
                        j.HasIndex(new[] { "AddOnId" }, "IX_DishAddOns_AddOnId");
                    });
        });

        modelBuilder.Entity<DishCategory>(entity =>
        {
            entity.HasIndex(e => e.ParentId, "IX_DishCategories_ParentId");

            entity.HasIndex(e => e.Slug, "UQ_DishCategories_1").IsUnique();

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Slug)
                .HasMaxLength(200)
                .IsUnicode(false);

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("FK_DishCategories_ParentId");
        });

        modelBuilder.Entity<DishImage>(entity =>
        {
            entity.HasIndex(e => e.DishId, "IX_DishImages_DishId");

            entity.HasIndex(e => e.MediaFileId, "IX_DishImages_MediaFileId");

            entity.HasIndex(e => new { e.DishId, e.MediaFileId }, "UQ_DishImages_1").IsUnique();

            entity.HasIndex(e => e.DishId, "UX_DishImages_Primary")
                .IsUnique()
                .HasFilter("([IsPrimary]=(1))");

            entity.HasOne(d => d.Dish).WithOne(p => p.DishImage)
                .HasForeignKey<DishImage>(d => d.DishId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DishImages_DishId");

            entity.HasOne(d => d.MediaFile).WithMany(p => p.DishImages)
                .HasForeignKey(d => d.MediaFileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DishImages_MediaFileId");
        });

        modelBuilder.Entity<DishReview>(entity =>
        {
            entity.HasIndex(e => e.DishId, "IX_DishReviews_DishId");

            entity.HasIndex(e => e.UserId, "IX_DishReviews_UserId");

            entity.HasIndex(e => new { e.DishId, e.UserId }, "UQ_DishReviews_1").IsUnique();

            entity.Property(e => e.Body).HasMaxLength(2000);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("PENDING");

            entity.HasOne(d => d.Dish).WithMany(p => p.DishReviews)
                .HasForeignKey(d => d.DishId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DishReviews_DishId");

            entity.HasOne(d => d.User).WithMany(p => p.DishReviews)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DishReviews_UserId");
        });

        modelBuilder.Entity<DishVariant>(entity =>
        {
            entity.HasIndex(e => e.DishId, "IX_DishVariants_DishId");

            entity.HasIndex(e => e.Sku, "UQ_DishVariants_1").IsUnique();

            entity.Property(e => e.IsAvailable).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Price).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.SellingUnit).HasMaxLength(30);
            entity.Property(e => e.Sku)
                .HasMaxLength(60)
                .IsUnicode(false);
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.Dish).WithMany(p => p.DishVariants)
                .HasForeignKey(d => d.DishId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DishVariants_DishId");
        });

        modelBuilder.Entity<Ingredient>(entity =>
        {
            entity.HasIndex(e => e.BaseUnitId, "IX_Ingredients_BaseUnitId");

            entity.HasIndex(e => e.Code, "UQ_Ingredients_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.ReorderLevel).HasColumnType("decimal(19, 4)");

            entity.HasOne(d => d.BaseUnit).WithMany(p => p.Ingredients)
                .HasForeignKey(d => d.BaseUnitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Ingredients_BaseUnitId");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("tr_Invoices_Protect"));

            entity.HasIndex(e => e.IssuedBy, "IX_Invoices_IssuedBy");

            entity.HasIndex(e => e.OrderId, "IX_Invoices_OrderId");

            entity.HasIndex(e => e.OrderId, "UQ_Invoices_1").IsUnique();

            entity.HasIndex(e => e.Number, "UQ_Invoices_2").IsUnique();

            entity.Property(e => e.CustomerAddress).HasMaxLength(500);
            entity.Property(e => e.CustomerName).HasMaxLength(200);
            entity.Property(e => e.CustomerTaxCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.ExternalInvoiceRef).HasMaxLength(100);
            entity.Property(e => e.GrandTotal)
                .HasComputedColumnSql("(([Subtotal]-[DiscountAmount])+[TaxAmount])", true)
                .HasColumnType("decimal(21, 2)");
            entity.Property(e => e.IssuedAtUtc).HasPrecision(3);
            entity.Property(e => e.Number)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("DRAFT");
            entity.Property(e => e.Subtotal).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.TaxAmount).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.IssuedByNavigation).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.IssuedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Invoices_IssuedBy");

            entity.HasOne(d => d.Order).WithOne(p => p.Invoice)
                .HasForeignKey<Invoice>(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Invoices_OrderId");
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("tr_InvoiceLines_Protect"));

            entity.HasIndex(e => e.InvoiceId, "IX_InvoiceLines_InvoiceId");

            entity.HasIndex(e => e.OrderItemId, "IX_InvoiceLines_OrderItemId");

            entity.HasIndex(e => new { e.InvoiceId, e.LineNumber }, "UQ_InvoiceLines_1").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(250);
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.LineSubtotal)
                .HasComputedColumnSql("(CONVERT([decimal](19,2),round([Quantity]*[UnitPrice],(2))))", true)
                .HasColumnType("decimal(19, 2)");
            entity.Property(e => e.LineTotal)
                .HasComputedColumnSql("((CONVERT([decimal](19,2),round([Quantity]*[UnitPrice],(2)))-[DiscountAmount])+[TaxAmount])", true)
                .HasColumnType("decimal(21, 2)");
            entity.Property(e => e.LineType)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Quantity).HasColumnType("decimal(12, 3)");
            entity.Property(e => e.TaxAmount).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.TaxRate).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.Unit).HasMaxLength(30);
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(19, 2)");

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceLines)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InvoiceLines_InvoiceId");

            entity.HasOne(d => d.OrderItem).WithMany(p => p.InvoiceLines)
                .HasForeignKey(d => d.OrderItemId)
                .HasConstraintName("FK_InvoiceLines_OrderItemId");
        });

        modelBuilder.Entity<MediaFile>(entity =>
        {
            entity.HasIndex(e => e.UploadedBy, "IX_MediaFiles_UploadedBy");

            entity.Property(e => e.AltText).HasMaxLength(255);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.MimeType)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.OriginalName).HasMaxLength(255);
            entity.Property(e => e.Url).HasMaxLength(1000);

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.MediaFiles)
                .HasForeignKey(d => d.UploadedBy)
                .HasConstraintName("FK_MediaFiles_UploadedBy");
        });

        modelBuilder.Entity<NavigationItem>(entity =>
        {
            entity.HasIndex(e => e.MenuId, "IX_NavigationItems_MenuId");

            entity.HasIndex(e => e.ParentId, "IX_NavigationItems_ParentId");

            entity.HasIndex(e => new { e.Id, e.MenuId }, "UQ_Navigation_IdMenu").IsUnique();

            entity.Property(e => e.IsVisible).HasDefaultValue(true);
            entity.Property(e => e.Label).HasMaxLength(100);
            entity.Property(e => e.Url).HasMaxLength(1000);

            entity.HasOne(d => d.Menu).WithMany(p => p.NavigationItems)
                .HasForeignKey(d => d.MenuId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NavigationItems_MenuId");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("FK_NavigationItems_ParentId");

            entity.HasOne(d => d.NavigationItemNavigation).WithMany(p => p.InverseNavigationItemNavigation)
                .HasPrincipalKey(p => new { p.Id, p.MenuId })
                .HasForeignKey(d => new { d.ParentId, d.MenuId })
                .HasConstraintName("FK_Navigation_ParentMenu");
        });

        modelBuilder.Entity<NavigationMenu>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_NavigationMenus_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.CreatedAtUtc }, "IX_Notifications_Unread").HasFilter("([ReadAtUtc] IS NULL)");

            entity.HasIndex(e => e.UserId, "IX_Notifications_UserId");

            entity.Property(e => e.Body).HasMaxLength(2000);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.LinkUrl).HasMaxLength(1000);
            entity.Property(e => e.ReadAtUtc).HasPrecision(3);
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Notifications_UserId");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasIndex(e => e.CreatedBy, "IX_Orders_CreatedBy");

            entity.HasIndex(e => e.CustomerId, "IX_Orders_CustomerId");

            entity.HasIndex(e => e.OpenedAtUtc, "IX_Orders_OpenedAt");

            entity.HasIndex(e => e.ReservationId, "IX_Orders_ReservationId");

            entity.HasIndex(e => e.Code, "UQ_Orders_1").IsUnique();

            entity.Property(e => e.ClosedAtUtc).HasPrecision(3);
            entity.Property(e => e.Code)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.DeliveryAddress).HasMaxLength(500);
            entity.Property(e => e.GuestName).HasMaxLength(150);
            entity.Property(e => e.GuestPhone)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.OpenedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.OrderType)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("OPEN");
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.OrderCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Orders_CreatedBy");

            entity.HasOne(d => d.Customer).WithMany(p => p.OrderCustomers)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK_Orders_CustomerId");

            entity.HasOne(d => d.Reservation).WithMany(p => p.Orders)
                .HasForeignKey(d => d.ReservationId)
                .HasConstraintName("FK_Orders_ReservationId");
        });

        modelBuilder.Entity<OrderDiscount>(entity =>
        {
            entity.HasIndex(e => e.ApprovedBy, "IX_OrderDiscounts_ApprovedBy");

            entity.HasIndex(e => e.OrderId, "IX_OrderDiscounts_OrderId");

            entity.HasIndex(e => e.VoucherId, "IX_OrderDiscounts_VoucherId");

            entity.Property(e => e.Amount).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.Description).HasMaxLength(250);

            entity.HasOne(d => d.ApprovedByNavigation).WithMany(p => p.OrderDiscounts)
                .HasForeignKey(d => d.ApprovedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderDiscounts_ApprovedBy");

            entity.HasOne(d => d.Order).WithMany(p => p.OrderDiscounts)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderDiscounts_OrderId");

            entity.HasOne(d => d.Voucher).WithMany(p => p.OrderDiscounts)
                .HasForeignKey(d => d.VoucherId)
                .HasConstraintName("FK_OrderDiscounts_VoucherId");
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasIndex(e => e.OrderId, "IX_OrderItems_OrderId");

            entity.HasIndex(e => e.VariantId, "IX_OrderItems_VariantId");

            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.NameSnapshot).HasMaxLength(250);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.Quantity).HasColumnType("decimal(12, 3)");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("NEW");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.Order).WithMany(p => p.OrderItems)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderItems_OrderId");

            entity.HasOne(d => d.Variant).WithMany(p => p.OrderItems)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderItems_VariantId");
        });

        modelBuilder.Entity<OrderItemAddOn>(entity =>
        {
            entity.HasIndex(e => e.AddOnId, "IX_OrderItemAddOns_AddOnId");

            entity.HasIndex(e => e.OrderItemId, "IX_OrderItemAddOns_OrderItemId");

            entity.Property(e => e.NameSnapshot).HasMaxLength(100);
            entity.Property(e => e.Quantity).HasColumnType("decimal(12, 3)");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(19, 2)");

            entity.HasOne(d => d.AddOn).WithMany(p => p.OrderItemAddOns)
                .HasForeignKey(d => d.AddOnId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderItemAddOns_AddOnId");

            entity.HasOne(d => d.OrderItem).WithMany(p => p.OrderItemAddOns)
                .HasForeignKey(d => d.OrderItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderItemAddOns_OrderItemId");
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.ToTable("OrderStatusHistory");

            entity.HasIndex(e => e.ChangedBy, "IX_OrderStatusHistory_ChangedBy");

            entity.HasIndex(e => e.OrderId, "IX_OrderStatusHistory_OrderId");

            entity.Property(e => e.ChangedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.FromStatus)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.ToStatus)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.HasOne(d => d.ChangedByNavigation).WithMany(p => p.OrderStatusHistories)
                .HasForeignKey(d => d.ChangedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderStatusHistory_ChangedBy");

            entity.HasOne(d => d.Order).WithMany(p => p.OrderStatusHistories)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderStatusHistory_OrderId");
        });

        modelBuilder.Entity<OrderTable>(entity =>
        {
            entity.HasIndex(e => e.OrderId, "IX_OrderTables_OrderId");

            entity.HasIndex(e => e.TableId, "IX_OrderTables_TableId");

            entity.HasIndex(e => e.TableId, "UX_OrderTables_Occupied")
                .IsUnique()
                .HasFilter("([ReleasedAtUtc] IS NULL)");

            entity.Property(e => e.AssignedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ReleasedAtUtc).HasPrecision(3);

            entity.HasOne(d => d.Order).WithMany(p => p.OrderTables)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderTables_OrderId");

            entity.HasOne(d => d.Table).WithOne(p => p.OrderTable)
                .HasForeignKey<OrderTable>(d => d.TableId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderTables_TableId");
        });

        modelBuilder.Entity<Page>(entity =>
        {
            entity.HasIndex(e => e.UpdatedBy, "IX_Pages_UpdatedBy");

            entity.HasIndex(e => e.Slug, "UQ_Pages_1").IsUnique();

            entity.Property(e => e.MetaDescription).HasMaxLength(300);
            entity.Property(e => e.Slug)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.Pages)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_Pages_UpdatedBy");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasIndex(e => e.MethodId, "IX_Payments_MethodId");

            entity.HasIndex(e => e.OrderId, "IX_Payments_OrderId");

            entity.HasIndex(e => e.ReceivedBy, "IX_Payments_ReceivedBy");

            entity.HasIndex(e => e.IdempotencyKey, "UQ_Payments_1").IsUnique();

            entity.HasIndex(e => new { e.Provider, e.ProviderTransactionId }, "UX_Payments_ProviderReference")
                .IsUnique()
                .HasFilter("([Provider] IS NOT NULL AND [ProviderTransactionId] IS NOT NULL)");

            entity.Property(e => e.Amount).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.PaidAtUtc).HasPrecision(3);
            entity.Property(e => e.Provider)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ProviderTransactionId)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Purpose)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("PENDING");

            entity.HasOne(d => d.Method).WithMany(p => p.Payments)
                .HasForeignKey(d => d.MethodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payments_MethodId");

            entity.HasOne(d => d.Order).WithMany(p => p.Payments)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payments_OrderId");

            entity.HasOne(d => d.ReceivedByNavigation).WithMany(p => p.Payments)
                .HasForeignKey(d => d.ReceivedBy)
                .HasConstraintName("FK_Payments_ReceivedBy");
        });

        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_PaymentMethods_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_Permissions_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(150);
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.HasIndex(e => e.AuthorId, "IX_Posts_AuthorId");

            entity.HasIndex(e => e.CategoryId, "IX_Posts_CategoryId");

            entity.HasIndex(e => e.CoverId, "IX_Posts_CoverId");

            entity.HasIndex(e => new { e.Status, e.PublishedAtUtc }, "IX_Posts_Public").IsDescending(false, true);

            entity.HasIndex(e => e.Slug, "UQ_Posts_1").IsUnique();

            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.PublishedAtUtc).HasPrecision(3);
            entity.Property(e => e.Slug)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("DRAFT");
            entity.Property(e => e.Summary).HasMaxLength(1000);
            entity.Property(e => e.Title).HasMaxLength(250);
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.Author).WithMany(p => p.Posts)
                .HasForeignKey(d => d.AuthorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Posts_AuthorId");

            entity.HasOne(d => d.Category).WithMany(p => p.Posts)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Posts_CategoryId");

            entity.HasOne(d => d.Cover).WithMany(p => p.Posts)
                .HasForeignKey(d => d.CoverId)
                .HasConstraintName("FK_Posts_CoverId");

            entity.HasMany(d => d.Tags).WithMany(p => p.Posts)
                .UsingEntity<Dictionary<string, object>>(
                    "PostTag",
                    r => r.HasOne<Tag>().WithMany()
                        .HasForeignKey("TagId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_PostTags_TagId"),
                    l => l.HasOne<Post>().WithMany()
                        .HasForeignKey("PostId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_PostTags_PostId"),
                    j =>
                    {
                        j.HasKey("PostId", "TagId");
                        j.ToTable("PostTags");
                        j.HasIndex(new[] { "TagId" }, "IX_PostTags_TagId");
                    });
        });

        modelBuilder.Entity<PostCategory>(entity =>
        {
            entity.HasIndex(e => e.ParentId, "IX_PostCategories_ParentId");

            entity.HasIndex(e => e.Slug, "UQ_PostCategories_1").IsUnique();

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Slug)
                .HasMaxLength(200)
                .IsUnicode(false);

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("FK_PostCategories_ParentId");
        });

        modelBuilder.Entity<PostComment>(entity =>
        {
            entity.HasIndex(e => e.ModeratedBy, "IX_PostComments_ModeratedBy");

            entity.HasIndex(e => e.ParentId, "IX_PostComments_ParentId");

            entity.HasIndex(e => e.PostId, "IX_PostComments_PostId");

            entity.HasIndex(e => e.UserId, "IX_PostComments_UserId");

            entity.HasIndex(e => new { e.Id, e.PostId }, "UQ_Comments_IdPost").IsUnique();

            entity.Property(e => e.Body).HasMaxLength(2000);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("PENDING");

            entity.HasOne(d => d.ModeratedByNavigation).WithMany(p => p.PostCommentModeratedByNavigations)
                .HasForeignKey(d => d.ModeratedBy)
                .HasConstraintName("FK_PostComments_ModeratedBy");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("FK_PostComments_ParentId");

            entity.HasOne(d => d.Post).WithMany(p => p.PostComments)
                .HasForeignKey(d => d.PostId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PostComments_PostId");

            entity.HasOne(d => d.User).WithMany(p => p.PostCommentUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PostComments_UserId");

            entity.HasOne(d => d.PostCommentNavigation).WithMany(p => p.InversePostCommentNavigation)
                .HasPrincipalKey(p => new { p.Id, p.PostId })
                .HasForeignKey(d => new { d.ParentId, d.PostId })
                .HasConstraintName("FK_Comments_ParentPost");
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasIndex(e => e.CreatedBy, "IX_PurchaseOrders_CreatedBy");

            entity.HasIndex(e => e.SupplierId, "IX_PurchaseOrders_SupplierId");

            entity.HasIndex(e => e.WarehouseId, "IX_PurchaseOrders_WarehouseId");

            entity.HasIndex(e => e.Number, "UQ_PurchaseOrders_1").IsUnique();

            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Number)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("DRAFT");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrders_CreatedBy");

            entity.HasOne(d => d.Supplier).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrders_SupplierId");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrders_WarehouseId");
        });

        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.HasIndex(e => e.IngredientId, "IX_PurchaseOrderItems_IngredientId");

            entity.HasIndex(e => e.PurchaseOrderId, "IX_PurchaseOrderItems_PurchaseOrderId");

            entity.HasIndex(e => new { e.PurchaseOrderId, e.IngredientId }, "UQ_PurchaseOrderItems_1").IsUnique();

            entity.Property(e => e.Quantity).HasColumnType("decimal(19, 4)");
            entity.Property(e => e.UnitCost).HasColumnType("decimal(19, 4)");

            entity.HasOne(d => d.Ingredient).WithMany(p => p.PurchaseOrderItems)
                .HasForeignKey(d => d.IngredientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrderItems_IngredientId");

            entity.HasOne(d => d.PurchaseOrder).WithMany(p => p.PurchaseOrderItems)
                .HasForeignKey(d => d.PurchaseOrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrderItems_PurchaseOrderId");
        });

        modelBuilder.Entity<Recipe>(entity =>
        {
            entity.HasIndex(e => e.VariantId, "IX_Recipes_VariantId");

            entity.HasIndex(e => new { e.VariantId, e.VersionNumber }, "UQ_Recipes_1").IsUnique();

            entity.HasIndex(e => e.VariantId, "UX_Recipes_Active")
                .IsUnique()
                .HasFilter("([IsActive]=(1))");

            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.YieldQuantity)
                .HasDefaultValue(1m)
                .HasColumnType("decimal(12, 3)");

            entity.HasOne(d => d.Variant).WithOne(p => p.Recipe)
                .HasForeignKey<Recipe>(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Recipes_VariantId");
        });

        modelBuilder.Entity<RecipeItem>(entity =>
        {
            entity.HasIndex(e => e.IngredientId, "IX_RecipeItems_IngredientId");

            entity.HasIndex(e => e.RecipeId, "IX_RecipeItems_RecipeId");

            entity.HasIndex(e => new { e.RecipeId, e.IngredientId }, "UQ_RecipeItems_1").IsUnique();

            entity.Property(e => e.Quantity).HasColumnType("decimal(19, 4)");
            entity.Property(e => e.WastePercent).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.Ingredient).WithMany(p => p.RecipeItems)
                .HasForeignKey(d => d.IngredientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RecipeItems_IngredientId");

            entity.HasOne(d => d.Recipe).WithMany(p => p.RecipeItems)
                .HasForeignKey(d => d.RecipeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RecipeItems_RecipeId");
        });

        modelBuilder.Entity<Refund>(entity =>
        {
            entity.HasIndex(e => e.ApprovedBy, "IX_Refunds_ApprovedBy");

            entity.HasIndex(e => e.PaymentId, "IX_Refunds_PaymentId");

            entity.HasIndex(e => e.IdempotencyKey, "UQ_Refunds_1").IsUnique();

            entity.Property(e => e.Amount).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RefundedAtUtc).HasPrecision(3);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("PENDING");

            entity.HasOne(d => d.ApprovedByNavigation).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.ApprovedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Refunds_ApprovedBy");

            entity.HasOne(d => d.Payment).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.PaymentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Refunds_PaymentId");
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasIndex(e => e.CreatedBy, "IX_Reservations_CreatedBy");

            entity.HasIndex(e => e.CustomerId, "IX_Reservations_CustomerId");

            entity.HasIndex(e => new { e.StartsAtUtc, e.EndsAtUtc }, "IX_Reservations_Time");

            entity.HasIndex(e => e.Code, "UQ_Reservations_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.EndsAtUtc).HasPrecision(3);
            entity.Property(e => e.GuestName).HasMaxLength(150);
            entity.Property(e => e.GuestPhone)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.StartsAtUtc).HasPrecision(3);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("PENDING");
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.ReservationCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_Reservations_CreatedBy");

            entity.HasOne(d => d.Customer).WithMany(p => p.ReservationCustomers)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK_Reservations_CustomerId");

            entity.HasMany(d => d.Tables).WithMany(p => p.Reservations)
                .UsingEntity<Dictionary<string, object>>(
                    "ReservationTable",
                    r => r.HasOne<DiningTable>().WithMany()
                        .HasForeignKey("TableId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_ReservationTables_TableId"),
                    l => l.HasOne<Reservation>().WithMany()
                        .HasForeignKey("ReservationId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_ReservationTables_ReservationId"),
                    j =>
                    {
                        j.HasKey("ReservationId", "TableId");
                        j.ToTable("ReservationTables");
                        j.HasIndex(new[] { "TableId" }, "IX_ReservationTables_TableId");
                    });
        });

        modelBuilder.Entity<ReservationService>(entity =>
        {
            entity.HasIndex(e => e.ReservationId, "IX_ReservationServices_ReservationId");

            entity.HasIndex(e => e.ServiceId, "IX_ReservationServices_ServiceId");

            entity.Property(e => e.NameSnapshot).HasMaxLength(150);
            entity.Property(e => e.Quantity).HasColumnType("decimal(12, 3)");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(19, 2)");

            entity.HasOne(d => d.Reservation).WithMany(p => p.ReservationServices)
                .HasForeignKey(d => d.ReservationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReservationServices_ReservationId");

            entity.HasOne(d => d.Service).WithMany(p => p.ReservationServices)
                .HasForeignKey(d => d.ServiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReservationServices_ServiceId");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_Roles_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Name).HasMaxLength(100);

            entity.HasMany(d => d.Permissions).WithMany(p => p.Roles)
                .UsingEntity<Dictionary<string, object>>(
                    "RolePermission",
                    r => r.HasOne<Permission>().WithMany()
                        .HasForeignKey("PermissionId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_RolePermissions_PermissionId"),
                    l => l.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_RolePermissions_RoleId"),
                    j =>
                    {
                        j.HasKey("RoleId", "PermissionId");
                        j.ToTable("RolePermissions");
                        j.HasIndex(new[] { "PermissionId" }, "IX_RolePermissions_PermissionId");
                    });
        });

        modelBuilder.Entity<Service>(entity =>
        {
            entity.HasIndex(e => e.Slug, "UQ_Services_1").IsUnique();

            entity.Property(e => e.BasePrice).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Slug)
                .HasMaxLength(200)
                .IsUnicode(false);
        });

        modelBuilder.Entity<SiteSetting>(entity =>
        {
            entity.HasKey(e => e.SettingKey);

            entity.Property(e => e.SettingKey)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
        });

        modelBuilder.Entity<StockCount>(entity =>
        {
            entity.HasIndex(e => e.AdjustmentDocumentId, "IX_StockCounts_AdjustmentDocumentId");

            entity.HasIndex(e => e.CreatedBy, "IX_StockCounts_CreatedBy");

            entity.HasIndex(e => e.WarehouseId, "IX_StockCounts_WarehouseId");

            entity.HasIndex(e => e.Number, "UQ_StockCounts_1").IsUnique();

            entity.HasIndex(e => e.AdjustmentDocumentId, "UX_Counts_Adjustment")
                .IsUnique()
                .HasFilter("([AdjustmentDocumentId] IS NOT NULL)");

            entity.Property(e => e.CountedAtUtc).HasPrecision(3);
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.Number)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("DRAFT");

            entity.HasOne(d => d.AdjustmentDocument).WithOne(p => p.StockCount)
                .HasForeignKey<StockCount>(d => d.AdjustmentDocumentId)
                .HasConstraintName("FK_StockCounts_AdjustmentDocumentId");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.StockCounts)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockCounts_CreatedBy");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StockCounts)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockCounts_WarehouseId");
        });

        modelBuilder.Entity<StockCountLine>(entity =>
        {
            entity.HasIndex(e => e.IngredientId, "IX_StockCountLines_IngredientId");

            entity.HasIndex(e => e.LotId, "IX_StockCountLines_LotId");

            entity.HasIndex(e => e.StockCountId, "IX_StockCountLines_StockCountId");

            entity.HasIndex(e => new { e.StockCountId, e.IngredientId, e.LotId }, "UQ_StockCountLines_1").IsUnique();

            entity.Property(e => e.ActualQuantity).HasColumnType("decimal(19, 4)");
            entity.Property(e => e.Difference)
                .HasComputedColumnSql("([ActualQuantity]-[ExpectedQuantity])", true)
                .HasColumnType("decimal(20, 4)");
            entity.Property(e => e.ExpectedQuantity).HasColumnType("decimal(19, 4)");

            entity.HasOne(d => d.Ingredient).WithMany(p => p.StockCountLines)
                .HasForeignKey(d => d.IngredientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockCountLines_IngredientId");

            entity.HasOne(d => d.Lot).WithMany(p => p.StockCountLineLots)
                .HasForeignKey(d => d.LotId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockCountLines_LotId");

            entity.HasOne(d => d.StockCount).WithMany(p => p.StockCountLines)
                .HasForeignKey(d => d.StockCountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockCountLines_StockCountId");

            entity.HasOne(d => d.StockLot).WithMany(p => p.StockCountLineStockLots)
                .HasPrincipalKey(p => new { p.Id, p.IngredientId })
                .HasForeignKey(d => new { d.LotId, d.IngredientId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CountLines_LotIngredient");
        });

        modelBuilder.Entity<StockDocument>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("tr_StockDocs_Protect"));

            entity.HasIndex(e => new { e.WarehouseId, e.Status }, "IX_StockDocs_WarehouseStatus");

            entity.HasIndex(e => e.CreatedBy, "IX_StockDocuments_CreatedBy");

            entity.HasIndex(e => e.OrderId, "IX_StockDocuments_OrderId");

            entity.HasIndex(e => e.PostedBy, "IX_StockDocuments_PostedBy");

            entity.HasIndex(e => e.PurchaseOrderId, "IX_StockDocuments_PurchaseOrderId");

            entity.HasIndex(e => e.ReversesDocumentId, "IX_StockDocuments_ReversesDocumentId");

            entity.HasIndex(e => e.WarehouseId, "IX_StockDocuments_WarehouseId");

            entity.HasIndex(e => e.Number, "UQ_StockDocuments_1").IsUnique();

            entity.HasIndex(e => e.IdempotencyKey, "UQ_StockDocuments_2").IsUnique();

            entity.HasIndex(e => e.ReversesDocumentId, "UX_StockDocs_Reversal")
                .IsUnique()
                .HasFilter("([ReversesDocumentId] IS NOT NULL)");

            entity.Property(e => e.DocumentType)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.Note).HasMaxLength(1000);
            entity.Property(e => e.Number)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.OccurredAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.PostedAtUtc).HasPrecision(3);
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("DRAFT");
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.StockDocumentCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDocuments_CreatedBy");

            entity.HasOne(d => d.Order).WithMany(p => p.StockDocuments)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("FK_StockDocuments_OrderId");

            entity.HasOne(d => d.PostedByNavigation).WithMany(p => p.StockDocumentPostedByNavigations)
                .HasForeignKey(d => d.PostedBy)
                .HasConstraintName("FK_StockDocuments_PostedBy");

            entity.HasOne(d => d.PurchaseOrder).WithMany(p => p.StockDocuments)
                .HasForeignKey(d => d.PurchaseOrderId)
                .HasConstraintName("FK_StockDocuments_PurchaseOrderId");

            entity.HasOne(d => d.ReversesDocument).WithOne(p => p.InverseReversesDocument)
                .HasForeignKey<StockDocument>(d => d.ReversesDocumentId)
                .HasConstraintName("FK_StockDocuments_ReversesDocumentId");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StockDocuments)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDocuments_WarehouseId");
        });

        modelBuilder.Entity<StockDocumentLine>(entity =>
        {
            entity.ToTable(tb => tb.HasTrigger("tr_StockLines_Protect"));

            entity.HasIndex(e => e.DocumentId, "IX_StockDocumentLines_DocumentId");

            entity.HasIndex(e => e.IngredientId, "IX_StockDocumentLines_IngredientId");

            entity.HasIndex(e => e.LotId, "IX_StockDocumentLines_LotId");

            entity.HasIndex(e => e.OrderItemId, "IX_StockDocumentLines_OrderItemId");

            entity.HasIndex(e => e.RecipeId, "IX_StockDocumentLines_RecipeId");

            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.QuantityDelta).HasColumnType("decimal(19, 4)");
            entity.Property(e => e.UnitCost).HasColumnType("decimal(19, 4)");

            entity.HasOne(d => d.Document).WithMany(p => p.StockDocumentLines)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDocumentLines_DocumentId");

            entity.HasOne(d => d.Ingredient).WithMany(p => p.StockDocumentLines)
                .HasForeignKey(d => d.IngredientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDocumentLines_IngredientId");

            entity.HasOne(d => d.Lot).WithMany(p => p.StockDocumentLineLots)
                .HasForeignKey(d => d.LotId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDocumentLines_LotId");

            entity.HasOne(d => d.OrderItem).WithMany(p => p.StockDocumentLines)
                .HasForeignKey(d => d.OrderItemId)
                .HasConstraintName("FK_StockDocumentLines_OrderItemId");

            entity.HasOne(d => d.Recipe).WithMany(p => p.StockDocumentLines)
                .HasForeignKey(d => d.RecipeId)
                .HasConstraintName("FK_StockDocumentLines_RecipeId");

            entity.HasOne(d => d.StockLot).WithMany(p => p.StockDocumentLineStockLots)
                .HasPrincipalKey(p => new { p.Id, p.IngredientId })
                .HasForeignKey(d => new { d.LotId, d.IngredientId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockLines_LotIngredient");
        });

        modelBuilder.Entity<StockLot>(entity =>
        {
            entity.HasIndex(e => e.IngredientId, "IX_StockLots_IngredientId");

            entity.HasIndex(e => e.SupplierId, "IX_StockLots_SupplierId");

            entity.HasIndex(e => new { e.IngredientId, e.LotCode }, "UQ_StockLots_1").IsUnique();

            entity.HasIndex(e => new { e.Id, e.IngredientId }, "UQ_StockLots_2").IsUnique();

            entity.Property(e => e.LotCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasOne(d => d.Ingredient).WithMany(p => p.StockLots)
                .HasForeignKey(d => d.IngredientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockLots_IngredientId");

            entity.HasOne(d => d.Supplier).WithMany(p => p.StockLots)
                .HasForeignKey(d => d.SupplierId)
                .HasConstraintName("FK_StockLots_SupplierId");
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_Suppliers_1").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.TaxCode)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(e => e.Slug, "UQ_Tags_1").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Slug)
                .HasMaxLength(150)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Unit>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_Units_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.NormalizedUserName, "UQ_Users_1").IsUnique();

            entity.HasIndex(e => e.NormalizedEmail, "UX_Users_Email")
                .IsUnique()
                .HasFilter("([NormalizedEmail] IS NOT NULL)");

            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LockoutEndUtc).HasPrecision(3);
            entity.Property(e => e.NormalizedEmail).HasMaxLength(254);
            entity.Property(e => e.NormalizedUserName).HasMaxLength(100);
            entity.Property(e => e.PasswordHash).HasMaxLength(512);
            entity.Property(e => e.SecurityStamp).HasDefaultValueSql("(newid())");
            entity.Property(e => e.UpdatedAtUtc).HasPrecision(3);
            entity.Property(e => e.UserName).HasMaxLength(100);
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserRole",
                    r => r.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_UserRoles_RoleId"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_UserRoles_UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("UserRoles");
                        j.HasIndex(new[] { "RoleId" }, "IX_UserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<UserAddress>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_UserAddresses_UserId");

            entity.HasIndex(e => e.UserId, "UX_UserAddresses_Default")
                .IsUnique()
                .HasFilter("([IsDefault]=(1))");

            entity.Property(e => e.AddressLine).HasMaxLength(500);
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.RecipientName).HasMaxLength(150);

            entity.HasOne(d => d.User).WithOne(p => p.UserAddress)
                .HasForeignKey<UserAddress>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserAddresses_UserId");
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.AvatarUrl).HasMaxLength(1000);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.Gender)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedAtUtc).HasPrecision(3);

            entity.HasOne(d => d.User).WithOne(p => p.UserProfile)
                .HasForeignKey<UserProfile>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserProfiles_UserId");
        });

        modelBuilder.Entity<UserToken>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_UserTokens_UserId");

            entity.HasIndex(e => e.TokenHash, "UQ_UserTokens_1").IsUnique();

            entity.Property(e => e.ConsumedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ExpiresAtUtc).HasPrecision(3);
            entity.Property(e => e.Purpose)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.TokenHash)
                .HasMaxLength(32)
                .IsFixedLength();

            entity.HasOne(d => d.User).WithMany(p => p.UserTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserTokens_UserId");
        });

        modelBuilder.Entity<VIngredientInventory>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vIngredientInventory");

            entity.Property(e => e.QuantityOnHand).HasColumnType("decimal(38, 4)");
        });

        modelBuilder.Entity<VInventoryBalance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vInventoryBalances");

            entity.Property(e => e.QuantityOnHand).HasColumnType("decimal(38, 4)");
        });

        modelBuilder.Entity<VOrderPaymentBalance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vOrderPaymentBalance");

            entity.Property(e => e.BalanceDue).HasColumnType("decimal(38, 2)");
            entity.Property(e => e.GrandTotal).HasColumnType("decimal(21, 2)");
            entity.Property(e => e.InvoiceStatus)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.NetPaid).HasColumnType("decimal(38, 2)");
            entity.Property(e => e.PaidAmount).HasColumnType("decimal(38, 2)");
            entity.Property(e => e.RefundedAmount).HasColumnType("decimal(38, 2)");
        });

        modelBuilder.Entity<Voucher>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_Vouchers_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.DiscountType)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.DiscountValue).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.EndsAtUtc).HasPrecision(3);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.MaxDiscount).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.MinimumOrder).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.StartsAtUtc).HasPrecision(3);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_Warehouses_1").IsUnique();

            entity.Property(e => e.Code)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

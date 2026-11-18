using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Data;

public partial class DoraFoodContext
{
    public virtual DbSet<ChatConversation> ChatConversations
    {
        get;
        set;
    } = null!;

    public virtual DbSet<ChatMessage> ChatMessages
    {
        get;
        set;
    } = null!;

    private static void ConfigureChatModels(
        ModelBuilder modelBuilder)
    {
        // =====================================
        // HỘI THOẠI
        // =====================================
        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.ToTable("ChatConversations", "dbo");

            entity.HasKey(item => item.Id)
                .HasName("PK_ChatConversations");

            entity.Property(item => item.Id)
                .ValueGeneratedOnAdd();

            entity.Property(item => item.Title)
                .HasMaxLength(200)
                .IsRequired()
                .HasDefaultValue("Tư vấn cùng DoraFood");

            entity.Property(item => item.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .IsRequired()
                .HasDefaultValue("OPEN");

            entity.Property(item => item.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(item => item.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(item => item.ClosedAtUtc)
                .HasPrecision(3);

            entity.Property(item => item.Version)
                .IsRowVersion();

            entity.HasIndex(item => item.ReservationId)
                .HasDatabaseName(
                    "UX_ChatConversations_Reservation")
                .IsUnique()
                .HasFilter("[ReservationId] IS NOT NULL");

            entity.HasIndex(item => item.CustomerId)
                .HasDatabaseName(
                    "UX_ChatConversations_OpenGeneral")
                .IsUnique()
                .HasFilter(
                    "[ReservationId] IS NULL AND [Status] = 'OPEN'");

            entity.HasIndex(item => new
            {
                item.CustomerId,
                item.UpdatedAtUtc,
                item.Id
            })
                .HasDatabaseName(
                    "IX_ChatConversations_Customer")
                .IsDescending(false, true, true);

            entity.HasIndex(item => new
            {
                item.Status,
                item.UpdatedAtUtc,
                item.Id
            })
                .HasDatabaseName(
                    "IX_ChatConversations_Management")
                .IsDescending(false, true, true)
                .IncludeProperties(item => new
                {
                    item.CustomerId,
                    item.ReservationId,
                    item.AssignedTo
                });

            entity.HasOne(item => item.Customer)
                .WithMany()
                .HasForeignKey(item => item.CustomerId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName(
                    "FK_ChatConversations_CustomerId");

            entity.HasOne(item => item.Reservation)
                .WithMany()
                .HasForeignKey(item => item.ReservationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName(
                    "FK_ChatConversations_ReservationId");

            entity.HasOne(item => item.AssignedToNavigation)
                .WithMany()
                .HasForeignKey(item => item.AssignedTo)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName(
                    "FK_ChatConversations_AssignedTo");
        });

        // =====================================
        // TIN NHẮN
        // =====================================
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("ChatMessages", "dbo");

            entity.HasKey(item => item.Id)
                .HasName("PK_ChatMessages");

            entity.Property(item => item.Id)
                .ValueGeneratedOnAdd();

            entity.Property(item => item.SenderType)
                .HasMaxLength(20)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(item => item.Body)
                .HasMaxLength(2000)
                .IsRequired();

            // Mã được tạo khi gửi tin, không tự thay
            // bằng mã khác nếu cùng yêu cầu được gửi lại.
            entity.Property(item => item.ClientMessageId)
                .ValueGeneratedNever();

            entity.Property(item => item.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(item => item.ReadAtUtc)
                .HasPrecision(3);

            entity.HasAlternateKey(item => new
            {
                item.ConversationId,
                item.SenderId,
                item.ClientMessageId
            })
                .HasName("UQ_ChatMessages_ClientMessage");

            entity.HasIndex(item => new
            {
                item.ConversationId,
                item.Id
            })
                .HasDatabaseName(
                    "IX_ChatMessages_Conversation");

            entity.HasIndex(item => new
            {
                item.ConversationId,
                item.SenderType,
                item.Id
            })
                .HasDatabaseName(
                    "IX_ChatMessages_Unread")
                .HasFilter("[ReadAtUtc] IS NULL");

            entity.HasOne(item => item.Conversation)
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(item => item.ConversationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName(
                    "FK_ChatMessages_ConversationId");

            // Người gửi tin nhắn.
            entity.HasOne(item => item.Sender)
                .WithMany()
                .HasForeignKey(item => item.SenderId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName(
                    "FK_ChatMessages_SenderId");

            // Ảnh đính kèm tin nhắn.
            entity.HasOne(item => item.MediaFile)
                .WithMany()
                .HasForeignKey(item => item.MediaFileId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName(
                    "FK_ChatMessages_MediaFiles");
        });
    }
}
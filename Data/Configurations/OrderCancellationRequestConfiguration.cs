using DoraFood.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoraFood.Data.Configurations;

public sealed class OrderCancellationRequestConfiguration
    : IEntityTypeConfiguration<OrderCancellationRequest>
{
    public void Configure(EntityTypeBuilder<OrderCancellationRequest> entity)
    {
        entity.ToTable("OrderCancellationRequests", "dbo", table =>
        {
            table.HasCheckConstraint("CK_OrderCancellationRequests_Status",
                "[Status] IN ('PENDING', 'APPROVED', 'REJECTED')");
            table.HasCheckConstraint("CK_OrderCancellationRequests_Reason",
                "LEN(LTRIM(RTRIM([Reason]))) > 0");
            table.HasCheckConstraint("CK_OrderCancellationRequests_ReviewNote",
                "[ReviewNote] IS NULL OR LEN(LTRIM(RTRIM([ReviewNote]))) > 0");
            table.HasCheckConstraint("CK_OrderCancellationRequests_ReviewTime",
                "[ReviewedAtUtc] IS NULL OR [ReviewedAtUtc] >= [RequestedAtUtc]");
            table.HasCheckConstraint("CK_OrderCancellationRequests_ReviewState", """
                ([Status] = 'PENDING'
                    AND [ReviewedBy] IS NULL
                    AND [ReviewedAtUtc] IS NULL
                    AND [ReviewNote] IS NULL)
                OR
                ([Status] = 'APPROVED'
                    AND [ReviewedBy] IS NOT NULL
                    AND [ReviewedAtUtc] IS NOT NULL)
                OR
                ([Status] = 'REJECTED'
                    AND [ReviewedBy] IS NOT NULL
                    AND [ReviewedAtUtc] IS NOT NULL
                    AND [ReviewNote] IS NOT NULL
                    AND LEN(LTRIM(RTRIM([ReviewNote]))) > 0)
                """);
        });

        entity.HasKey(e => e.Id).HasName("PK_OrderCancellationRequests");
        entity.Property(e => e.Id).ValueGeneratedOnAdd();
        entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
        entity.Property(e => e.Status)
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired()
            .HasDefaultValue("PENDING");
        entity.Property(e => e.RequestedAtUtc)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");
        entity.Property(e => e.ReviewedAtUtc).HasPrecision(3);
        entity.Property(e => e.ReviewNote).HasMaxLength(500);
        entity.Property(e => e.Version).IsRowVersion();

        entity.HasIndex(e => e.OrderId, "UX_OrderCancellationRequests_OnePendingPerOrder")
            .IsUnique()
            .HasFilter("[Status] = 'PENDING'");
        entity.HasIndex(e => new { e.OrderId, e.RequestedAtUtc, e.Id },
                "IX_OrderCancellationRequests_OrderHistory")
            .IsDescending(false, true, true);
        entity.HasIndex(e => new { e.Status, e.RequestedAtUtc, e.Id },
                "IX_OrderCancellationRequests_StatusQueue")
            .IncludeProperties(e => new { e.OrderId, e.RequestedBy });
        entity.HasIndex(e => new { e.RequestedBy, e.RequestedAtUtc, e.Id },
                "IX_OrderCancellationRequests_Requester")
            .IsDescending(false, true, true)
            .IncludeProperties(e => new { e.OrderId, e.Status });

        // Quan hệ một chiều: không cần sửa các entity Order và User đang có.
        entity.HasOne(e => e.Order).WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_OrderCancellationRequests_Order");
        entity.HasOne(e => e.Requester).WithMany()
            .HasForeignKey(e => e.RequestedBy)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_OrderCancellationRequests_Requester");
        entity.HasOne(e => e.Reviewer).WithMany()
            .HasForeignKey(e => e.ReviewedBy)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_OrderCancellationRequests_Reviewer");
    }
}

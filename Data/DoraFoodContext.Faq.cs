using DoraFood.Models;
using DoraFood.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Data;

public partial class DoraFoodContext
{
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureFaqItems(modelBuilder);
    }

    private static void ConfigureFaqItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FaqItem>(entity =>
        {
            entity.ToTable("FaqItems", "dbo");

            entity.HasKey(item => item.Id)
                .HasName("PK_FaqItems");

            entity.Property(item => item.Question)
                .HasMaxLength(250)
                .IsRequired();

            entity.Property(item => item.Answer)
                .HasMaxLength(2000)
                .IsRequired();

            entity.Property(item => item.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(item => item.Version)
                .IsRowVersion();

            entity.HasIndex(item => new
            {
                item.IsActive,
                item.SortOrder,
                item.Id
            })
                .HasDatabaseName("IX_FaqItems_IsActive_SortOrder");
        });
    }
}
using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccess.Configurations;

public class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        builder.ToTable("Inventories");
        builder.HasQueryFilter(inventory => inventory.IsActive);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ProductId)
            .IsRequired();

        builder.Property(x => x.WarehouseId)
            .IsRequired();

        builder.Property(x => x.Quantity)
            .IsRequired()
            .HasDefaultValue(0);
        builder.HasCheckConstraint("CK_Inventories_Quantity_NonNegative", "[Quantity] >= 0");

        builder.Property(x => x.Version)
            .IsRequired()
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedOnAdd();
        builder.Property(x => x.Version)
            .IsConcurrencyToken();

        builder.HasIndex(x => new { x.ProductId, x.WarehouseId })
            .IsUnique();

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnType("datetime2")
            .IsRequired(false);

        builder.Property(x => x.DeactivatedAtUtc)
            .HasColumnType("datetime2")
            .IsRequired(false);

        builder.Property(x => x.CreatedByUserId)
            .HasColumnType("uniqueidentifier")
            .IsRequired(false);

        builder.Property(x => x.UpdatedByUserId)
            .HasColumnType("uniqueidentifier")
            .IsRequired(false);

        builder.Property(x => x.DeactivatedByUserId)
            .HasColumnType("uniqueidentifier")
            .IsRequired(false);
    }
}

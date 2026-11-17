using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccess.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasQueryFilter(stockMovement => stockMovement.IsActive);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.InventoryId)
            .IsRequired();

        builder.Property(x => x.MovementType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.Quantity)
            .IsRequired();
        builder.HasCheckConstraint("CK_StockMovements_Quantity_Positive", "[Quantity] > 0");

        builder.Property(x => x.QuantityBefore)
            .IsRequired();
        builder.HasCheckConstraint("CK_StockMovements_QuantityBefore_NonNegative", "[QuantityBefore] >= 0");

        builder.Property(x => x.QuantityAfter)
            .IsRequired();
        builder.HasCheckConstraint("CK_StockMovements_QuantityAfter_NonNegative", "[QuantityAfter] >= 0");

        builder.Property(x => x.Note)
            .HasMaxLength(500)
            .HasColumnType("nvarchar(500)")
            .IsRequired(false);

        builder.HasOne(x => x.Inventory)
            .WithMany()
            .HasForeignKey(x => x.InventoryId)
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

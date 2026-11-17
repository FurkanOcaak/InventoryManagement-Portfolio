using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccess.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");
        builder.HasQueryFilter(warehouse => warehouse.IsActive);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnType("varchar(50)");
        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)");

        builder.Property(x => x.Location)
            .HasMaxLength(300)
            .HasColumnType("nvarchar(300)")
            .IsRequired(false);

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

using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccess.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasQueryFilter(product => product.IsActive);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Barcode)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnType("varchar(50)");
        builder.HasIndex(x => x.Barcode)
            .IsUnique();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)");

        builder.Property(x => x.Description)
            .HasMaxLength(1000)
            .HasColumnType("nvarchar(1000)")
            .IsRequired(false);

        builder.Property(x => x.UnitPrice)
            .HasColumnType("decimal(18,2)")
            .HasPrecision(18, 2);
        builder.HasCheckConstraint("CK_Products_UnitPrice_NonNegative", "[UnitPrice] >= 0");

        builder.Property(x => x.MinimumStock)
            .IsRequired();
        builder.HasCheckConstraint("CK_Products_MinimumStock_NonNegative", "[MinimumStock] >= 0");

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

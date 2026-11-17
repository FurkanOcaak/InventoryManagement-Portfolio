using Entities.Common;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccess.Configurations;

public sealed class CommerceConfiguration : IEntityTypeConfiguration<ShoppingCart>, IEntityTypeConfiguration<CartItem>,
    IEntityTypeConfiguration<Order>, IEntityTypeConfiguration<OrderItem>
{
    private static void Audit<T>(EntityTypeBuilder<T> builder) where T : BaseEntity
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
        builder.Property(entity => entity.CreatedAtUtc).HasColumnType("datetime2");
        builder.Property(entity => entity.UpdatedAtUtc).HasColumnType("datetime2");
        builder.Property(entity => entity.DeactivatedAtUtc).HasColumnType("datetime2");
    }

    public void Configure(EntityTypeBuilder<ShoppingCart> builder)
    {
        builder.ToTable("ShoppingCarts");
        Audit(builder);
        builder.HasIndex(cart => cart.CustomerId).IsUnique();
        builder.Property(cart => cart.Version).IsConcurrencyToken().ValueGeneratedNever();
        builder.HasOne<Customer>().WithOne().HasForeignKey<ShoppingCart>(cart => cart.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(cart => cart.Items).WithOne().HasForeignKey(item => item.CartId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", table => table.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] >= 1 AND [Quantity] <= 10000"));
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.CartId, item.ProductId }).IsUnique();
        builder.HasOne<Product>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_Total", "[Total] >= 0");
            table.HasCheckConstraint("CK_Orders_Status", "[Status] = 'Pending'");
        });
        Audit(builder);
        builder.Property(order => order.Number).HasMaxLength(36).IsRequired();
        builder.Property(order => order.Status).HasMaxLength(20).IsRequired();
        builder.Property(order => order.Total).HasPrecision(18, 2);
        builder.HasIndex(order => order.Number).IsUnique();
        builder.HasIndex(order => new { order.CustomerId, order.IdempotencyKey }).IsUnique();
        builder.HasIndex(order => new { order.CustomerId, order.CartVersion }).IsUnique();
        builder.HasIndex(order => new { order.CustomerId, order.CreatedAtUtc });
        builder.HasOne<Customer>().WithMany().HasForeignKey(order => order.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Items).WithOne().HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "[Quantity] >= 1 AND [Quantity] <= 10000");
            table.HasCheckConstraint("CK_OrderItems_Prices", "[UnitPrice] >= 0 AND [Subtotal] >= 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.ProductBarcode).HasMaxLength(50).IsRequired();
        builder.Property(item => item.UnitPrice).HasPrecision(18, 2);
        builder.Property(item => item.Subtotal).HasPrecision(18, 2);
        builder.HasIndex(item => new { item.OrderId, item.ProductId }).IsUnique();
        builder.HasOne<Product>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

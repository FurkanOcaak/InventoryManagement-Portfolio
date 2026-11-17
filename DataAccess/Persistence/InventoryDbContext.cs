using Microsoft.EntityFrameworkCore;
using Entities.Entities;
using DataAccess.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace DataAccess.Persistence;

public class InventoryDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers { get; set; } = null!;

    public DbSet<Product> Products { get; set; } = null!;

    public DbSet<Warehouse> Warehouses { get; set; } = null!;

    public DbSet<Inventory> Inventories { get; set; } = null!;

    public DbSet<StockMovement> StockMovements { get; set; } = null!;

    public DbSet<ShoppingCart> ShoppingCarts { get; set; } = null!;
    public DbSet<CartItem> CartItems { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;

    public DbSet<DemoSeedRun> DemoSeedRuns { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasOne(user => user.Customer).WithOne()
                .HasForeignKey<ApplicationUser>(user => user.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(user => user.CustomerId).IsUnique()
                .HasFilter("[CustomerId] IS NOT NULL");
        });

        modelBuilder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.Property(login => login.LoginProvider).HasMaxLength(128);
            entity.Property(login => login.ProviderKey).HasMaxLength(128);
        });
        modelBuilder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.Property(token => token.LoginProvider).HasMaxLength(128);
            entity.Property(token => token.Name).HasMaxLength(128);
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }
}

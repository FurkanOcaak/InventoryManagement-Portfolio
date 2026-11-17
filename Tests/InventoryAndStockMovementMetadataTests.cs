using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class InventoryAndStockMovementMetadataTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public InventoryAndStockMovementMetadataTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    [Fact]
    public void InventoryConfiguration_UsesExpectedUniqueIndexAndQuantityConstraint()
    {
        using var context = CreateContext();
        var model = context.Model;
        var inventoryEntity = model.FindEntityType(typeof(Inventory));
        var script = context.Database.GenerateCreateScript();

        Assert.NotNull(inventoryEntity);
        Assert.Equal("Inventories", inventoryEntity.GetTableName());
        Assert.True(inventoryEntity.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).Contains("ProductId") && i.Properties.Select(p => p.Name).Contains("WarehouseId")));
        Assert.Contains("[Quantity] >= 0", script, StringComparison.Ordinal);
        Assert.NotNull(inventoryEntity.FindProperty(nameof(Inventory.Version)));
        Assert.True(inventoryEntity.FindProperty(nameof(Inventory.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public void StockMovementConfiguration_UsesExpectedCheckConstraintsAndDeleteBehavior()
    {
        using var context = CreateContext();
        var model = context.Model;
        var stockMovementEntity = model.FindEntityType(typeof(StockMovement));
        var inventoryRelationship = stockMovementEntity?.GetForeignKeys().SingleOrDefault(x => x.PrincipalEntityType.ClrType == typeof(Inventory));
        var script = context.Database.GenerateCreateScript();

        Assert.NotNull(stockMovementEntity);
        Assert.Equal("StockMovements", stockMovementEntity.GetTableName());
        Assert.Contains("[Quantity] > 0", script, StringComparison.Ordinal);
        Assert.Contains("[QuantityBefore] >= 0", script, StringComparison.Ordinal);
        Assert.Contains("[QuantityAfter] >= 0", script, StringComparison.Ordinal);
        Assert.NotNull(inventoryRelationship);
        Assert.Equal(DeleteBehavior.Restrict, inventoryRelationship!.DeleteBehavior);
    }

    private InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new InventoryDbContext(options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

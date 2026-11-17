using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class ProductAndWarehouseConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public ProductAndWarehouseConfigurationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    [Fact]
    public void ProductConfiguration_UsesExpectedTableNameAndUniqueConstraints()
    {
        using var context = CreateContext();

        var model = context.Model;
        var productEntity = model.FindEntityType(typeof(Product));

        Assert.NotNull(productEntity);
        Assert.Equal("Products", productEntity.GetTableName());
        Assert.True(productEntity.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).Contains("Barcode")));
        Assert.Equal(50, productEntity.FindProperty(nameof(Product.Barcode))!.GetMaxLength());
        Assert.Equal(200, productEntity.FindProperty(nameof(Product.Name))!.GetMaxLength());
        Assert.Equal(1000, productEntity.FindProperty(nameof(Product.Description))!.GetMaxLength());
        Assert.True(productEntity.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).Contains("Barcode")));
    }

    [Fact]
    public void WarehouseConfiguration_UsesExpectedTableNameAndUniqueConstraints()
    {
        using var context = CreateContext();

        var model = context.Model;
        var warehouseEntity = model.FindEntityType(typeof(Warehouse));

        Assert.NotNull(warehouseEntity);
        Assert.Equal("Warehouses", warehouseEntity.GetTableName());
        Assert.True(warehouseEntity.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).Contains("Code")));
        Assert.Equal(50, warehouseEntity.FindProperty(nameof(Warehouse.Code))!.GetMaxLength());
        Assert.Equal(200, warehouseEntity.FindProperty(nameof(Warehouse.Name))!.GetMaxLength());
        Assert.Equal(300, warehouseEntity.FindProperty(nameof(Warehouse.Location))!.GetMaxLength());
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

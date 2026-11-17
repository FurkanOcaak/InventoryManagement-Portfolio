using DataAccess.Persistence;
using DataAccess.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests;

public class ShoppingMigrationTests
{
    [Fact]
    public void Migration_OnlyCreatesFourNewTablesAndIndexes()
    {
        var migration = new AddCustomerShoppingAndOrders();
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "CartItems", "OrderItems", "Orders", "ShoppingCarts" }, tables.Select(table => table.Name).Order());
        foreach (var fk in tables.SelectMany(table => table.ForeignKeys).Where(fk => fk.PrincipalTable is "Products" or "Customers"))
            Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        Assert.All(migration.DownOperations, operation => Assert.Contains(Assert.IsType<DropTableOperation>(operation).Name,
            new[] { "CartItems", "OrderItems", "Orders", "ShoppingCarts" }));
    }

    [Fact]
    public void Migration_EnforcesOwnershipDeduplicationAndMoneyLimits()
    {
        var indexes = new AddCustomerShoppingAndOrders().UpOperations.OfType<CreateIndexOperation>().ToArray();
        foreach (var name in new[] { "IX_ShoppingCarts_CustomerId", "IX_CartItems_CartId_ProductId", "IX_Orders_Number",
            "IX_Orders_CustomerId_IdempotencyKey", "IX_Orders_CustomerId_CartVersion", "IX_OrderItems_OrderId_ProductId" })
            Assert.True(indexes.Single(index => index.Name == name).IsUnique);
        var tables = new AddCustomerShoppingAndOrders().UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.False(tables.Single(table => table.Name == "Orders").Columns.Single(column => column.Name == "CustomerId").IsNullable);
        Assert.Equal(18, tables.Single(table => table.Name == "Orders").Columns.Single(column => column.Name == "Total").Precision);
        Assert.Contains(tables.Single(table => table.Name == "Orders").CheckConstraints, check => check.Sql.Contains("Pending"));
    }

    [Fact]
    public void SnapshotAndSqlServerScript_AreSafeWithoutConnecting()
    {
        using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused;Database=SchemaOnly;Integrated Security=True;TrustServerCertificate=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript("20261117080122_LinkIdentityUserToCustomer");
        foreach (var unsafeSql in new[] { "DROP TABLE", "DELETE FROM", "UPDATE [", "ALTER TABLE [Products]", "ALTER TABLE [AspNetUsers]", "INSERT INTO [AspNetUsers]", "INSERT INTO [Orders]" })
            Assert.DoesNotContain(unsafeSql, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [ShoppingCarts]", sql);
        Assert.Contains("CREATE TABLE [Orders]", sql);
    }
}

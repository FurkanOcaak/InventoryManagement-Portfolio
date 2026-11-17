using DataAccess.Persistence;
using DataAccess.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests;

public class PackageFGMigrationTests
{
    [Fact]
    public void BarcodeMigration_OnlyRenamesExistingColumnAndUniqueIndex()
    {
        var migration = new RenameProductSkuToBarcode();
        Assert.Equal(2, migration.UpOperations.Count);
        var column = Assert.IsType<RenameColumnOperation>(migration.UpOperations[0]);
        Assert.Equal(("Products", "Sku", "Barcode"), (column.Table, column.Name, column.NewName));
        var index = Assert.IsType<RenameIndexOperation>(migration.UpOperations[1]);
        Assert.Equal(("IX_Products_Sku", "IX_Products_Barcode"), (index.Name, index.NewName));
        Assert.All(migration.DownOperations, operation => Assert.True(operation is RenameColumnOperation or RenameIndexOperation));
    }

    [Fact]
    public void CustomerLinkMigration_OnlyAddsNullableUniqueRestrictedForeignKey()
    {
        var operations = new LinkIdentityUserToCustomer().UpOperations;
        Assert.Equal(3, operations.Count);
        var column = Assert.IsType<AddColumnOperation>(operations[0]);
        Assert.Equal(("AspNetUsers", "CustomerId"), (column.Table, column.Name));
        Assert.True(column.IsNullable);
        Assert.Null(column.DefaultValue);
        var index = Assert.IsType<CreateIndexOperation>(operations[1]);
        Assert.True(index.IsUnique);
        Assert.Equal("[CustomerId] IS NOT NULL", index.Filter);
        var foreignKey = Assert.IsType<AddForeignKeyOperation>(operations[2]);
        Assert.Equal("Customers", foreignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete);
    }

    [Fact]
    public void SqlServerScriptAndSnapshot_AreSafeAndMatchCurrentModelWithoutConnecting()
    {
        using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused;Database=ScriptOnly;Integrated Security=True;TrustServerCertificate=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript("20261116195511_AddIdentityUsersAndRoles");
        Assert.Contains("sp_rename", sql);
        Assert.Contains("[CustomerId] IS NOT NULL", sql);
        Assert.Contains("FOREIGN KEY", sql);
        foreach (var unsafeSql in new[] { "DROP TABLE", "DROP COLUMN", "DELETE FROM", "UPDATE [Products]", "INSERT INTO [AspNetUsers]" })
            Assert.DoesNotContain(unsafeSql, sql, StringComparison.OrdinalIgnoreCase);
    }
}

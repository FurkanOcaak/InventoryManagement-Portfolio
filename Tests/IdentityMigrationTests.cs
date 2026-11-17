using DataAccess.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests;

public class IdentityMigrationTests
{
    [Fact]
    public void Up_ShouldOnlyCreateIdentityTablesAndIndexes()
    {
        var migration = new AddIdentityUsersAndRoles();
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(7, tables.Length);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.True(operation is CreateTableOperation or CreateIndexOperation);
            var table = operation is CreateTableOperation create
                ? create.Name : ((CreateIndexOperation)operation).Table;
            Assert.StartsWith("AspNet", table);
        });
        Assert.Equal(typeof(Guid), tables.Single(table => table.Name == "AspNetUsers")
            .Columns.Single(column => column.Name == "Id").ClrType);
        Assert.Equal(typeof(Guid), tables.Single(table => table.Name == "AspNetRoles")
            .Columns.Single(column => column.Name == "Id").ClrType);
        var boundedKeys = tables.Where(table => table.Name is "AspNetUserLogins" or "AspNetUserTokens")
            .SelectMany(table => table.Columns.Where(column => column.ClrType == typeof(string)
                && table.PrimaryKey!.Columns.Contains(column.Name)));
        Assert.All(boundedKeys, column => Assert.Equal(128, column.MaxLength));
    }

    [Fact]
    public void Down_ShouldOnlyDropNewIdentityTables()
    {
        var migration = new AddIdentityUsersAndRoles();
        Assert.Equal(7, migration.DownOperations.Count);
        Assert.All(migration.DownOperations, operation =>
            Assert.StartsWith("AspNet", Assert.IsType<DropTableOperation>(operation).Name));
    }
}

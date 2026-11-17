using Business.Common.Security;
using DataAccess.Identity;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class IdentityInfrastructureTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task IdentityModel_ShouldCreateExpectedSqliteTablesWithGuidKeys()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);

        Assert.Equal(typeof(Guid), context.Model.FindEntityType(typeof(ApplicationUser))!
            .FindPrimaryKey()!.Properties.Single().ClrType);
        Assert.Equal(typeof(Guid), context.Model.FindEntityType(typeof(IdentityRole<Guid>))!
            .FindPrimaryKey()!.Properties.Single().ClrType);

        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        using var reader = await command.ExecuteReaderAsync();
        var tables = new HashSet<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

        string[] expectedTables =
        [
            "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
            "AspNetUserLogins", "AspNetUserTokens", "AspNetRoleClaims",
            "Customers", "Products", "Warehouses", "Inventories", "StockMovements"
        ];
        Assert.All(expectedTables, table => Assert.Contains(table, tables));
    }

    [Fact]
    public async Task IdentityManagers_ShouldResolveStoresAndReadSqliteWithoutCreatingAccounts()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        Assert.Empty(await userManager.Users.ToListAsync());
        Assert.Empty(await roleManager.Roles.ToListAsync());
    }

    [Fact]
    public void CurrentUser_ShouldBeScopedAndAnonymousOutsideHttpRequests()
    {
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var currentUser = firstScope.ServiceProvider.GetRequiredService<ICurrentUser>();

        Assert.Same(currentUser, firstScope.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.NotSame(currentUser, secondScope.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public async Task IdentityRegistration_ShouldEnableOnlyStandardIdentityCookieSchemes()
    {
        using var scope = factory.Services.CreateScope();
        var schemes = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();

        var names = (await schemes.GetAllSchemesAsync()).Select(scheme => scheme.Name).Order().ToArray();
        string[] expected =
        [
            IdentityConstants.ApplicationScheme, IdentityConstants.ExternalScheme,
            IdentityConstants.TwoFactorRememberMeScheme, IdentityConstants.TwoFactorUserIdScheme
        ];
        Assert.Equal(expected.Order(), names);
    }
}

using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using DataAccess.Interceptors;
using DataAccess.Persistence;

namespace Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Test hosts must not depend on permission to write the Windows Event Log.
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting("ConnectionStrings:InventoryDatabase",
            "Server=(localdb)\\MSSQLLocalDB;Database=Inventory_Test;Trusted_Connection=True;");
        _connection ??= new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:InventoryDatabase"] = "Server=(localdb)\\MSSQLLocalDB;Database=Inventory_Test;Trusted_Connection=True;"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Real cookie/CSRF cryptography with keys isolated to this test host.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
            services.RemoveAll<InventoryDbContext>();
            services.RemoveAll<DbConnection>();

            services.AddSingleton<DbConnection>(_connection);
            services.RemoveAll<AuditSaveChangesInterceptor>();
            services.AddScoped<AuditSaveChangesInterceptor>();

            services.AddDbContext<InventoryDbContext>((serviceProvider, options) =>
            {
                var connection = serviceProvider.GetRequiredService<DbConnection>();

                options.UseSqlite(connection);
                options.AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
            });

            using var scope = services.BuildServiceProvider().CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            if (!string.Equals(
                    dbContext.Database.ProviderName,
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Test database provider must be SQLite. Actual provider: {dbContext.Database.ProviderName ?? "<null>"}");
            }

            dbContext.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }

        base.Dispose(disposing);
    }
}

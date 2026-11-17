using System.Diagnostics;
using System.Text.Json;
using DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace InventoryManagement.AccountProvisioning;

public interface IAccountProvisioningTarget
{
    Task<bool> ValidateAsync(CancellationToken cancellationToken = default);
}

public sealed class AccountProvisioningTarget(InventoryDbContext context, IHostEnvironment environment)
    : IAccountProvisioningTarget
{
    private static readonly string[] ExpectedMigrations =
    [
        "20261112135135_InitialCreateCustomers", "20261115121034_AddProductsAndWarehouses",
        "20261115130410_AddInventoryAndStockMovements", "20261116195511_AddIdentityUsersAndRoles",
        "20261117080026_RenameProductSkuToBarcode", "20261117080122_LinkIdentityUserToCustomer"
    ];
    private static readonly string[] ExpectedIdentityTables =
    [
        "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins",
        "AspNetUserRoles", "AspNetUsers", "AspNetUserTokens"
    ];

    public static bool IsExpectedConnection(string connectionString)
    {
        try
        {
            var parsed = new SqlConnectionStringBuilder(connectionString);
            return parsed.InitialCatalog == "InventoryManagement_Dev"
                && parsed.DataSource.ToLowerInvariant() is "localhost,14331" or "127.0.0.1,14331"
                    or "tcp:localhost,14331" or "tcp:127.0.0.1,14331";
        }
        catch { return false; }
    }

    public static bool IsExpectedDockerInspection(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() != 1) return false;
            var container = doc.RootElement[0];
            return container.GetProperty("Name").GetString() == "/inventory_sqlserver"
                && container.GetProperty("State").GetProperty("Running").GetBoolean()
                && container.GetProperty("State").GetProperty("Health").GetProperty("Status").GetString() == "healthy"
                && container.GetProperty("Config").GetProperty("Labels").GetProperty("com.docker.compose.project").GetString() == "inventorymanagement"
                && container.GetProperty("NetworkSettings").GetProperty("Ports").GetProperty("1433/tcp")
                    .EnumerateArray().Any(port => port.GetProperty("HostIp").GetString() == "127.0.0.1"
                        && port.GetProperty("HostPort").GetString() == "14331")
                && container.GetProperty("Mounts").EnumerateArray().Count(mount =>
                    mount.GetProperty("Destination").GetString() == "/var/opt/mssql"
                    && mount.GetProperty("Type").GetString() == "volume"
                    && mount.GetProperty("Name").GetString() == "inventorymanagement_inventory_sql_data") == 1;
        }
        catch { return false; }
    }

    public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() || context.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer"
            || !IsExpectedConnection(context.Database.GetConnectionString() ?? string.Empty)) return false;
        try
        {
            var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "inspect", "--type", "container", "inventory_sqlserver" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await error; // Discard diagnostics; inspection may include secrets. Never print it.
            if (process.ExitCode != 0 || !IsExpectedDockerInspection(await output)) return false;
            await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT CASE WHEN DB_NAME()=N'InventoryManagement_Dev' AND DATABASEPROPERTYEX(DB_NAME(),'Status')=N'ONLINE' THEN 1 ELSE 0 END";
                if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1) return false;
                if (!(await context.Database.GetAppliedMigrationsAsync(cancellationToken)).SequenceEqual(ExpectedMigrations)) return false;
                var tables = await context.Database.SqlQueryRaw<string>(
                    "SELECT name AS Value FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name LIKE N'AspNet%' ORDER BY name").ToListAsync(cancellationToken);
                if (!tables.SequenceEqual(ExpectedIdentityTables)) return false;
                command.CommandText = """
                    SELECT CASE WHEN
                    EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AspNetUsers') AND name=N'CustomerId' AND is_nullable=1)
                    AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AspNetUsers') AND name=N'IX_AspNetUsers_CustomerId' AND is_unique=1 AND is_disabled=0 AND filter_definition=N'([CustomerId] IS NOT NULL)')
                    AND EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AspNetUsers_Customers_CustomerId' AND delete_referential_action=0 AND is_disabled=0 AND is_not_trusted=0)
                    AND EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId=u.Id JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name=N'Admin' AND r.NormalizedName=N'ADMIN' AND u.CustomerId IS NULL AND u.PasswordHash IS NOT NULL)
                    THEN 1 ELSE 0 END
                    """;
                return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
            }
            finally { await context.Database.CloseConnectionAsync(); }
        }
        catch { return false; }
    }
}

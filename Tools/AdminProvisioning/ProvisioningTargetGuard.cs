using System.Diagnostics;
using DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.AdminProvisioning;

public sealed class ProvisioningTargetGuard(InventoryDbContext context)
{
    private static readonly string[] ExpectedMigrations =
    [
        "20261112135135_InitialCreateCustomers",
        "20261115121034_AddProductsAndWarehouses",
        "20261115130410_AddInventoryAndStockMovements",
        "20261116195511_AddIdentityUsersAndRoles"
    ];
    private static readonly string[] ExpectedTables =
    [
        "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins",
        "AspNetUserRoles", "AspNetUsers", "AspNetUserTokens"
    ];

    public static bool IsExpectedConnection(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var server = builder.DataSource.ToLowerInvariant();
            return builder.InitialCatalog == "InventoryManagement_Dev"
                && server is "localhost,14331" or "127.0.0.1,14331"
                    or "tcp:localhost,14331" or "tcp:127.0.0.1,14331";
        }
        catch { return false; }
    }

    public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer"
            || !IsExpectedConnection(context.Database.GetConnectionString() ?? string.Empty)) return false;
        if (!await VerifyDockerAsync(cancellationToken)) return false;
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT state_desc FROM sys.databases WHERE name=DB_NAME();";
            if (await command.ExecuteScalarAsync(cancellationToken) is not string state || state != "ONLINE")
                return false;
            var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
            if (!applied.SequenceEqual(ExpectedMigrations)) return false;
            var tables = await context.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sys.tables WHERE name LIKE N'AspNet%' ORDER BY name;")
                .ToListAsync(cancellationToken);
            if (!tables.SequenceEqual(ExpectedTables)) return false;
            return !await context.Users.AnyAsync(cancellationToken);
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private static async Task<bool> VerifyDockerAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "inspect", "--type", "container", "--format",
            "{{.State.Status}}|{{.State.Health.Status}}|{{index .Config.Labels \"com.docker.compose.project\"}}|{{range .Mounts}}{{if eq .Destination \"/var/opt/mssql\"}}{{.Name}}{{end}}{{end}}",
            "inventory_sqlserver"
        }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process is null) return false;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await error; // Intentionally discard raw diagnostics.
        return process.ExitCode == 0 && (await output).Trim()
            == "running|healthy|inventorymanagement|inventorymanagement_inventory_sql_data";
    }
}

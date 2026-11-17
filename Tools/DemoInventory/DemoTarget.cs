using System.Diagnostics;
using InventoryManagement.AccountProvisioning;
using DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.DemoInventory;

public static class DemoTarget
{
    public static readonly string[] ExistingMigrations =
    [
        "20261112135135_InitialCreateCustomers", "20261115121034_AddProductsAndWarehouses",
        "20261115130410_AddInventoryAndStockMovements", "20261116195511_AddIdentityUsersAndRoles",
        "20261117080026_RenameProductSkuToBarcode", "20261117080122_LinkIdentityUserToCustomer",
        "20261117103152_AddCustomerShoppingAndOrders"
    ];

    public static bool AcceptMigrations(string[] applied, string[] compiled, bool apply)
        => compiled.Length == 8 && compiled.Take(7).SequenceEqual(ExistingMigrations)
            && compiled[7].EndsWith("_AddDemoSeedLedger", StringComparison.Ordinal)
            && (applied.SequenceEqual(compiled) || (!apply && applied.SequenceEqual(ExistingMigrations)));

    public static async Task<bool> ValidateAsync(InventoryDbContext db, bool apply, CancellationToken ct = default)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer"
            || !AccountProvisioningTarget.IsExpectedConnection(db.Database.GetConnectionString() ?? "")) return false;
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "inspect", "--type", "container", "inventory_sqlserver" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process is null) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(); throw; }
        await error; // Never print docker inspect: it contains secret environment values.
        if (process.ExitCode != 0 || !AccountProvisioningTarget.IsExpectedDockerInspection(await output)) return false;
        if (!AcceptMigrations((await db.Database.GetAppliedMigrationsAsync(ct)).ToArray(), db.Database.GetMigrations().ToArray(), apply)) return false;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT CASE WHEN DB_NAME()=N'InventoryManagement_Dev' AND DATABASEPROPERTYEX(DB_NAME(),'Status')=N'ONLINE' THEN 1 ELSE 0 END";
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}

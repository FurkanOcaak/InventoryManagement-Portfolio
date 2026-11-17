using System.Text.Json;
using DataAccess.Interceptors;
using DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebAPI.Controllers;

namespace InventoryManagement.DemoInventory;

public static class DemoCommand
{
    public static bool? Parse(string[] args) => args.Length == 0 ? false
        : args.Length == 1 ? args[0] switch { "--dry-run" => false, "--apply" => true, _ => null } : null;
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var apply = DemoCommand.Parse(args);
        if (apply is null)
        { Console.Error.WriteLine("Usage: DemoInventory [--dry-run | --apply]. No credentials in arguments."); return 2; }
        try
        {
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            if (!builder.Environment.IsDevelopment()
                || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") is string asp && asp != "Development")
            { Console.Error.WriteLine("Development environment required. No writes."); return 2; }
            if (apply.Value && (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected))
            { Console.Error.WriteLine("Apply requires an interactive local console."); return 2; }
            builder.Configuration.AddUserSecrets(typeof(AuthController).Assembly, optional: false).AddEnvironmentVariables();
            var connection = builder.Configuration.GetConnectionString("InventoryDatabase");
            if (connection is null) { Console.Error.WriteLine("Configured connection required."); return 2; }
            await using var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(connection).AddInterceptors(new AuditSaveChangesInterceptor()).Options);
            if (!await DemoTarget.ValidateAsync(db, apply.Value))
            { Console.Error.WriteLine("Target/container/volume/migration verification failed. No writes."); return 3; }
            bool ledgerAvailable = (await db.Database.GetAppliedMigrationsAsync()).Count() == 8;
            var before = await DataSnapshot.CaptureAsync(db);
            var saved = ledgerAvailable ? await db.DemoSeedRuns.AsNoTracking().SingleOrDefaultAsync(r => r.DatasetId == DemoPlanner.DatasetId) : null;
            var plan = saved is null ? await DemoPlanner.CreateAsync(db)
                : JsonSerializer.Deserialize<DemoPlan>(saved.PlanJson) ?? throw new InvalidOperationException();
            if (saved is not null)
            {
                if (saved.PlanHash != plan.Hash) throw new InvalidOperationException();
                await new DemoSeeder(db).VerifyDatasetAsync(plan);
            }
            Console.WriteLine($"Verified Development / InventoryManagement_Dev / inventory_sqlserver / expected volume / {(ledgerAvailable ? 8 : 7)} migrations.");
            Console.WriteLine($"Dataset: {DemoPlanner.DatasetId}; plan SHA256: {plan.Hash}");
            Console.WriteLine($"Add: products {(saved is null ? plan.Products.Length : 0)}, warehouses {(saved is null ? plan.Warehouses.Length : 0)}, movements {(saved is null ? plan.Movements.Length : 0)}.");
            Console.WriteLine($"Planned totals: products {plan.ExistingProducts + plan.Products.Length}, warehouses {plan.ExistingWarehouses + plan.Warehouses.Length}, movements {plan.ExistingMovements + plan.Movements.Length}.");
            Console.WriteLine($"Existing protected rows: {string.Join(", ", before.Tables.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value.Count}"))}.");
            Console.WriteLine($"Stock-in events: {plan.Movements.Count(m => m.Type == Entities.Entities.MovementType.StockIn)}; stock-out events: {plan.Movements.Count(m => m.Type == Entities.Entities.MovementType.StockOut)}.");
            foreach (var w in plan.Warehouses) Console.WriteLine($"Warehouse: {w.Code} — {w.Name}; {w.Location}");
            foreach (var p in plan.Products) Console.WriteLine($"Product: {p.Barcode} — {p.Name}; {p.Price:0.00} TRY; minimum {p.Minimum}");
            Console.WriteLine("Existing products/stock/users/customer links/orders/carts are preserved; no human audit actor is invented.");
            if (!apply.Value)
            {
                before.VerifyPreserved(await DataSnapshot.CaptureAsync(db));
                Console.WriteLine(ledgerAvailable ? "DRY-RUN: read only; fingerprints unchanged." : "DRY-RUN: read only; fingerprints unchanged. Ledger migration requires separate approval before apply.");
                return 0;
            }
            Console.WriteLine("Requires a separately verified current full backup and an approved maintenance window. No automatic backup, migration, retry or repair.");
            Console.Write($"Type APPLY {DemoPlanner.DatasetId} {plan.Hash} to approve this exact plan: ");
            if (Console.ReadLine() != $"APPLY {DemoPlanner.DatasetId} {plan.Hash}")
            { Console.WriteLine("Cancelled. No writes."); return 4; }
            if (!await DemoTarget.ValidateAsync(db, apply: true)) throw new InvalidOperationException();
            var result = await new DemoSeeder(db).ApplyAsync(plan.Hash, confirmed: true);
            Console.WriteLine(result.AlreadyApplied ? "Already applied; zero additions. Dataset integrity verified."
                : "Committed once. Dataset integrity and all pre-existing row fingerprints verified.");
            return 0;
        }
        catch
        {
            // Connection strings, row values and SQL diagnostics must not escape.
            Console.Error.WriteLine("Stopped safely. No automatic retry/restore/reset. Inspect state before a separate attempt.");
            return 5;
        }
    }
}

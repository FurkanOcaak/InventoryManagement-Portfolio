using DataAccess.Interceptors;
using DataAccess.Persistence;
using InventoryManagement.AccountProvisioning;
using InventoryManagement.DemoInventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using WebAPI.Controllers;

namespace Tests;

public sealed class PackageJSqlFactAttribute : FactAttribute
{
    public PackageJSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PACKAGE_J_SQL_TESTS") != "1")
            Skip = "Opt in with PACKAGE_J_SQL_TESTS=1; uses a new isolated PackageJ_Test_<guid> database only.";
    }
}

public sealed class DemoInventorySqlTests
{
    [PackageJSqlFact]
    public async Task IsolatedSqlServer_MigrationsRollbackConcurrentImportAndPersistentReapply()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets(typeof(AuthController).Assembly, optional: false).Build();
        var source = configuration.GetConnectionString("InventoryDatabase") ?? throw new InvalidOperationException("Local test configuration missing.");
        Assert.True(AccountProvisioningTarget.IsExpectedConnection(source));
        string database = "PackageJ_Test_" + Guid.NewGuid().ToString("N");
        var masterOptions = new SqlConnectionStringBuilder(source) { InitialCatalog = "master", Pooling = false };
        var testOptions = new SqlConnectionStringBuilder(source) { InitialCatalog = database, Pooling = false };
        Assert.StartsWith("PackageJ_Test_", testOptions.InitialCatalog);
        Assert.NotEqual("InventoryManagement_Dev", testOptions.InitialCatalog);
        await using var master = new SqlConnection(masterOptions.ConnectionString);
        await master.OpenAsync();
        await using (var create = master.CreateCommand())
        { create.CommandText = $"CREATE DATABASE [{database}]"; await create.ExecuteNonQueryAsync(); }
        try
        {
            await using (var db = Context(testOptions.ConnectionString))
            {
                await db.Database.MigrateAsync();
                Assert.Equal(8, (await db.Database.GetAppliedMigrationsAsync()).Count());
                await DemoInventoryTests.PopulateAsync(db);
            }
            DemoPlan plan;
            DataSnapshot baseline;
            await using (var db = Context(testOptions.ConnectionString))
            { plan = await DemoPlanner.CreateAsync(db); baseline = await DataSnapshot.CaptureAsync(db); }
            await using (var failing = Context(testOptions.ConnectionString, new FailAfterSeveralSaves()))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoSeeder(failing).ApplyAsync(plan.Hash, true));
                baseline.VerifyPreserved(await DataSnapshot.CaptureAsync(failing));
                Assert.Empty(await failing.DemoSeedRuns.ToListAsync());
            }
            // Two independent connections/process-equivalent contexts compete for one ledger PK.
            async Task<bool> Attempt()
            {
                await using var db = Context(testOptions.ConnectionString);
                try { await new DemoSeeder(db).ApplyAsync(plan.Hash, true); return true; }
                catch (DemoSeedBusyException) { return false; } // Immediate rejection, no retries.
                catch (SqlException) { return false; } // A deadlock victim stops; never retry.
                catch (DbUpdateException) { return false; } // Unique/concurrency collision also stops.
            }
            var results = await Task.WhenAll(Attempt(), Attempt());
            Assert.Contains(true, results);
            await using (var db = Context(testOptions.ConnectionString))
            {
                baseline.VerifyPreserved(await DataSnapshot.CaptureAsync(db), plan);
                Assert.Single(await db.DemoSeedRuns.ToListAsync());
                Assert.Equal(180, await db.StockMovements.CountAsync());
                var complete = await DataSnapshot.CaptureAsync(db);
                Assert.True((await new DemoSeeder(db).ApplyAsync(plan.Hash, true)).AlreadyApplied);
                complete.VerifyPreserved(await DataSnapshot.CaptureAsync(db));
            }
        }
        finally
        {
            // This literal prefix is checked again before deleting only our random test DB.
            if (!database.StartsWith("PackageJ_Test_", StringComparison.Ordinal) || database.Length != 46)
                throw new InvalidOperationException("Unexpected test cleanup target.");
            await using var drop = master.CreateCommand();
            drop.CommandText = $"DROP DATABASE [{database}]";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static InventoryDbContext Context(string connection, SaveChangesInterceptor? extra = null)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor());
        if (extra is not null) options.AddInterceptors(extra);
        return new(options.Options);
    }
    private sealed class FailAfterSeveralSaves : SaveChangesInterceptor
    {
        private int calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++calls == 6) throw new InvalidOperationException("Injected SQL test failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

using System.Text.Json;
using DataAccess.Identity;
using DataAccess.Interceptors;
using DataAccess.Persistence;
using DataAccess.Persistence.Migrations;
using DataAccess.Repositories;
using Entities.Entities;
using InventoryManagement.DemoInventory;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests;

public sealed class DemoInventoryTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private InventoryDbContext db = null!;
    private readonly FailDuringSeed failure = new();

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(), failure).Options);
        await db.Database.EnsureCreatedAsync();
        await PopulateAsync(db);
        db.ChangeTracker.Clear();
    }
    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }

    internal static async Task PopulateAsync(InventoryDbContext context)
    {
        var products = Enumerable.Range(1, 4).Select(i => new Product
        { Id = Guid.NewGuid(), Barcode = $"KEEP-{i}", Name = $"Existing product {i}", UnitPrice = 250m, MinimumStock = 10 }).ToArray();
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = "KEEP-W1", Name = "Existing warehouse" };
        context.Products.AddRange(products);
        context.Warehouses.AddRange(warehouse, new Warehouse { Code = "KEEP-W2", Name = "Inactive warehouse", IsActive = false });
        var inventory = new Inventory { Id = Guid.NewGuid(), ProductId = products[0].Id, WarehouseId = warehouse.Id, Quantity = 100, Version = Guid.NewGuid() };
        context.Inventories.Add(inventory);
        for (int i = 0; i < 10; i++) context.StockMovements.Add(new StockMovement
        { InventoryId = inventory.Id, MovementType = MovementType.StockIn, Quantity = 10, QuantityBefore = i * 10, QuantityAfter = (i + 1) * 10, Note = "Existing real movement" });
        var customer = new Customer { Id = Guid.NewGuid(), Code = "MUS-TEST-001", Name = "Synthetic protected customer" };
        context.Customers.Add(customer);
        foreach (var roleName in new[] { "Admin", "WarehouseStaff", "Customer" })
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"fixture-{roleName}",
                PasswordHash = "isolated-test-hash", SecurityStamp = Guid.NewGuid().ToString(), CustomerId = roleName == "Customer" ? customer.Id : null };
            var role = new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() };
            context.Users.Add(user); context.Roles.Add(role);
            context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        }
        var order = new Order { CustomerId = customer.Id, Number = "FIXTURE-ORDER", Total = 250m,
            IdempotencyKey = Guid.NewGuid(), CartVersion = Guid.NewGuid(), Items = [new OrderItem
            { ProductId = products[0].Id, ProductBarcode = products[0].Barcode, ProductName = products[0].Name, Quantity = 1, UnitPrice = 250m, Subtotal = 250m }] };
        context.Orders.Add(order);
        context.ShoppingCarts.Add(new ShoppingCart { CustomerId = customer.Id, Items = [new CartItem { ProductId = products[1].Id, Quantity = 2 }] });
        await context.SaveChangesAsync();
    }

    [Fact] public async Task Preview_AdaptsToAllExistingCountsIncludingInactive()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        Assert.Equal((36, 3, 170), (plan.Products.Length, plan.Warehouses.Length, plan.Movements.Length));
        Assert.Equal((4, 2, 10), (plan.ExistingProducts, plan.ExistingWarehouses, plan.ExistingMovements));
    }
    [Fact] public async Task Preview_IsReadOnlyAndDeterministic()
    {
        var before = await DataSnapshot.CaptureAsync(db);
        var first = await DemoPlanner.CreateAsync(db);
        Assert.Equal(first.Hash, (await DemoPlanner.CreateAsync(db)).Hash);
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(db));
        Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact] public async Task Catalog_HasUniqueSyntheticStringBarcodesWithLeadingZeros()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        Assert.Equal(36, plan.Products.Select(p => p.Barcode).Distinct().Count());
        Assert.All(plan.Products, p => { Assert.StartsWith("000-DEMO-J-", p.Barcode); Assert.Contains("GS1 tahsisi yoktur", p.Description); Assert.True(p.Price > 0 && p.Minimum > 0); });
    }
    [Fact] public async Task WarehousePlans_AreDistinctAndSynthetic()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        Assert.Equal(3, plan.Warehouses.Select(w => w.Name).Distinct().Count());
        Assert.All(plan.Warehouses, w => Assert.StartsWith("Sentetik tesis", w.Location));
    }
    [Fact] public async Task Plan_MovementsHavePositiveQuantitiesAndContinuousBalances()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        foreach (var group in plan.Movements.GroupBy(m => m.InventoryId))
        {
            int before = 0;
            Assert.Equal(MovementType.StockIn, group.First().Type);
            foreach (var move in group)
            {
                Assert.Equal(before, move.Before); Assert.True(move.Quantity > 0);
                Assert.Equal(move.Type == MovementType.StockIn ? move.Before + move.Quantity : move.Before - move.Quantity, move.After);
                Assert.True(move.After >= 0); before = move.After;
            }
        }
        Assert.Equal(4, plan.Movements.Select(m => m.WarehouseId).Distinct().Count());
        Assert.Contains(plan.Movements, m => m.Type == MovementType.StockOut);
    }
    [Fact] public async Task Apply_CreatesProductsWarehousesMovementsAndLedgerTogether()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        var result = await new DemoSeeder(db).ApplyAsync(plan.Hash, true);
        Assert.False(result.AlreadyApplied); Assert.True(result.ExistingDataPreserved);
        Assert.Equal(40, await db.Products.IgnoreQueryFilters().CountAsync());
        Assert.Equal(5, await db.Warehouses.IgnoreQueryFilters().CountAsync());
        Assert.Equal(180, await db.StockMovements.IgnoreQueryFilters().CountAsync());
        Assert.Equal(37, await db.Inventories.CountAsync());
        var ledger = await db.DemoSeedRuns.SingleAsync();
        Assert.Equal(plan.Hash, ledger.PlanHash); Assert.True(ledger.CompletedAtUtc != default);
    }
    [Fact] public async Task Reapply_FromFreshContext_DoesNotDuplicateOrIncreaseStock()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        await new DemoSeeder(db).ApplyAsync(plan.Hash, true);
        var before = await DataSnapshot.CaptureAsync(db);
        await using var fresh = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor()).Options);
        Assert.True((await new DemoSeeder(fresh).ApplyAsync(plan.Hash, true)).AlreadyApplied);
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(fresh));
        Assert.Single(await fresh.DemoSeedRuns.ToListAsync());
    }
    [Fact] public async Task Apply_PreservesEveryExistingScalarIncludingOrdersCartsAndIdentity()
    {
        var before = await DataSnapshot.CaptureAsync(db);
        var plan = await DemoPlanner.CreateAsync(db);
        await new DemoSeeder(db).ApplyAsync(plan.Hash, true);
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(db), plan);
        Assert.Equal("Pending", (await db.Orders.SingleAsync()).Status);
        Assert.Equal(3, await db.Users.CountAsync()); Assert.Equal(3, await db.UserRoles.CountAsync());
        Assert.Equal(1, await db.CartItems.CountAsync());
    }
    [Fact] public async Task Audit_UsesRealInterceptorWithNoInventedHumanActor()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        await new DemoSeeder(db).ApplyAsync(plan.Hash, true);
        foreach (var p in await db.Products.Where(p => p.Barcode.StartsWith("000-DEMO-J-")).ToListAsync())
        { Assert.Null(p.CreatedByUserId); Assert.NotEqual(default, p.CreatedAtUtc); }
        var movements = await db.StockMovements.Where(m => m.Note!.StartsWith("DEMO:")).ToListAsync();
        Assert.All(movements, m => { Assert.Null(m.CreatedByUserId); Assert.Contains(DemoPlanner.DatasetId, m.Note!); Assert.NotEqual(default, m.CreatedAtUtc); });
        await new DemoSeeder(db).VerifyDatasetAsync(plan);
    }
    [Fact] public async Task LowStock_UsesExistingDashboardQueryAlongsideHighStock()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        await new DemoSeeder(db).ApplyAsync(plan.Hash, true);
        var low = await new StockRepository(db).GetLowStockAsync(default);
        Assert.Equal(12, low.Count);
        Assert.All(low, item => Assert.True(item.Quantity < item.MinimumStock));
        Assert.Contains(await db.Inventories.ToListAsync(), i => i.Quantity > 100);
    }
    [Fact] public async Task WithoutConfirmation_NothingIsWritten()
    {
        var before = await DataSnapshot.CaptureAsync(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoSeeder(db).ApplyAsync("unused", false));
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(db));
    }
    [Fact] public async Task StalePreview_IsRejectedWithoutWrites()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        db.Products.Add(new Product { Barcode = "CONCURRENT", Name = "New independent product" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await DataSnapshot.CaptureAsync(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoSeeder(db).ApplyAsync(plan.Hash, true));
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(db)); Assert.Empty(await db.DemoSeedRuns.ToListAsync());
    }
    [Fact] public async Task MidMovementFailure_RollsBackProductsWarehousesStockAndLedger()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        var before = await DataSnapshot.CaptureAsync(db);
        failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoSeeder(db).ApplyAsync(plan.Hash, true));
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(db)); Assert.Empty(await db.DemoSeedRuns.ToListAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }
    [Theory]
    [InlineData("barcode")][InlineData("warehouse")][InlineData("movement")][InlineData("inventory")]
    public async Task PersistentIdentityCollision_FailsWithoutAdoptingExistingRecords(string kind)
    {
        var plan = await DemoPlanner.CreateAsync(db);
        if (kind == "barcode") db.Products.Add(new Product { Barcode = plan.Products[0].Barcode.ToLowerInvariant(), Name = "Unowned" });
        if (kind == "warehouse") db.Warehouses.Add(new Warehouse { Code = plan.Warehouses[0].Code, Name = "Unowned" });
        var existing = await db.Inventories.SingleAsync();
        if (kind == "movement") db.StockMovements.Add(new StockMovement { Id = plan.Movements[0].Id, InventoryId = existing.Id, MovementType = MovementType.StockIn, Quantity = 1 });
        if (kind == "inventory") db.Inventories.Add(new Inventory { Id = plan.Movements[0].InventoryId, ProductId = (await db.Products.OrderBy(p => p.Barcode).Skip(1).FirstAsync()).Id, WarehouseId = existing.WarehouseId, Version = Guid.NewGuid() });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await DataSnapshot.CaptureAsync(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoPlanner.CreateAsync(db));
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(db));
    }
    [Fact] public async Task ChangedDataset_IsRejectedWithoutRepair()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        await new DemoSeeder(db).ApplyAsync(plan.Hash, true);
        var movement = await db.StockMovements.SingleAsync(m => m.Id == plan.Movements[0].Id);
        movement.Note = "Independent change"; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await DataSnapshot.CaptureAsync(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoSeeder(db).ApplyAsync(plan.Hash, true));
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(db));
    }
    [Fact] public async Task LedgerHash_CannotBeChangedToTriggerReapply()
    {
        var plan = await DemoPlanner.CreateAsync(db);
        await new DemoSeeder(db).ApplyAsync(plan.Hash, true);
        (await db.DemoSeedRuns.SingleAsync()).PlanHash = "tampered"; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await DataSnapshot.CaptureAsync(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoSeeder(db).ApplyAsync(plan.Hash, true));
        before.VerifyPreserved(await DataSnapshot.CaptureAsync(db));
    }
    [Fact] public async Task ExistingInventory_VersionStillRejectsAStaleWriter()
    {
        var inventory = await db.Inventories.SingleAsync();
        await using var other = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options);
        var stale = await other.Inventories.SingleAsync();
        inventory.Quantity++; inventory.Version = Guid.NewGuid(); await db.SaveChangesAsync();
        stale.Quantity++; stale.Version = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
    }
    [Fact] public void Migration_OnlyAddsLedgerAndSnapshotMatches()
    {
        var operation = Assert.IsType<CreateTableOperation>(Assert.Single(new AddDemoSeedLedger().UpOperations));
        Assert.Equal("DemoSeedRuns", operation.Name); Assert.Empty(operation.ForeignKeys);
        using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(
            "Server=unused;Database=SchemaOnly;Integrated Security=True;TrustServerCertificate=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var script = context.GetService<IMigrator>().GenerateScript(DemoTarget.ExistingMigrations[^1]);
        Assert.Contains("CREATE TABLE [DemoSeedRuns]", script);
        foreach (var word in new[] { "UPDATE ", "DELETE ", "DROP ", "INSERT INTO [Products]", "INSERT INTO [Orders]" }) Assert.DoesNotContain(word, script);
    }
    [Theory][InlineData(0, false)][InlineData(7, false)][InlineData(8, true)]
    public void TargetGate_RequiresExactHistoryBeforeWrites(int count, bool expected)
    {
        var all = DemoTarget.ExistingMigrations.Concat(["20261117161249_AddDemoSeedLedger"]).ToArray();
        Assert.Equal(expected, DemoTarget.AcceptMigrations(all.Take(count).ToArray(), all, apply: true));
        Assert.True(DemoTarget.AcceptMigrations(all.Take(7).ToArray(), all, apply: false));
        Assert.False(DemoTarget.AcceptMigrations(all.Reverse().ToArray(), all, apply: true));
    }
    [Theory][InlineData("--dry-run", false)][InlineData("--apply", true)][InlineData("--force", null)]
    public void Command_RejectsUndocumentedModes(string argument, bool? expected)
    { Assert.Equal(expected, DemoCommand.Parse([argument])); Assert.False(DemoCommand.Parse([])); Assert.Null(DemoCommand.Parse(["--apply", "--force"])); }
    [Fact] public async Task SaturatedTargets_StopRatherThanTouchingExistingStock()
    {
        for (int i = 4; i < 40; i++) db.Products.Add(new Product { Barcode = $"OTHER-{i}", Name = "Existing" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoPlanner.CreateAsync(db));
    }
    private sealed class FailDuringSeed : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        private int calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && ++calls == 6) throw new InvalidOperationException("Injected isolated-test failure after partial saves.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

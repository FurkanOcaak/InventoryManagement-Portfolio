using System.Data;
using System.Text.Json;
using Business.Features.Stock.Abstractions;
using Business.Features.Stock.DTOs;
using Business.Features.Stock.Services;
using Business.Features.Stock.Validators;
using DataAccess.Persistence;
using DataAccess.Repositories;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InventoryManagement.DemoInventory;

public sealed record SeedResult(bool AlreadyApplied, DemoPlan Plan, bool ExistingDataPreserved);
public sealed class DemoSeedBusyException() : InvalidOperationException("Another demo import is active. No retry.");

public sealed class DemoSeeder(InventoryDbContext db)
{
    public async Task<SeedResult> ApplyAsync(string approvedPlanHash, bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw new InvalidOperationException("Explicit confirmation required.");
        if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("A clean context is required.");
        // SQL Server SERIALIZABLE locks ledger key ranges; the PK and deterministic entity
        // IDs remain independent collision barriers. A losing caller stops without retry.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (db.Database.IsSqlServer())
            {
                // Fail immediately instead of letting two importers upgrade shared range locks.
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.Transaction = tx.GetDbTransaction();
                command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'inventory-demo-j-v1', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @r;";
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new DemoSeedBusyException();
            }
            var old = await db.DemoSeedRuns.AsNoTracking().SingleOrDefaultAsync(r => r.DatasetId == DemoPlanner.DatasetId, ct);
            if (old is not null)
            {
                var saved = JsonSerializer.Deserialize<DemoPlan>(old.PlanJson) ?? throw new InvalidOperationException("Invalid ledger.");
                if (saved.Hash != old.PlanHash || approvedPlanHash != old.PlanHash) throw new InvalidOperationException("Ledger hash mismatch.");
                await VerifyDatasetAsync(saved, ct);
                await tx.CommitAsync(ct);
                return new(true, saved, true);
            }
            var plan = await DemoPlanner.CreateAsync(db, ct);
            if (plan.Hash != approvedPlanHash) throw new InvalidOperationException("Database changed after preview. Review a new plan.");
            var before = await DataSnapshot.CaptureAsync(db, ct);
            foreach (var p in plan.Products)
                db.Products.Add(new Product { Id = p.Id, Barcode = p.Barcode, Name = p.Name,
                    Description = p.Description, UnitPrice = p.Price, MinimumStock = p.Minimum });
            foreach (var w in plan.Warehouses)
                db.Warehouses.Add(new Warehouse { Id = w.Id, Code = w.Code, Name = w.Name, Location = w.Location });
            await db.SaveChangesAsync(ct);
            foreach (var movement in plan.Movements)
            {
                // Reuse the application's validation, overflow, insufficient-stock,
                // version and movement arithmetic. This adapter joins our outer transaction.
                var service = new StockService(new TransactionStockRepository(db, movement), new StockOperationRequestValidator());
                var request = new StockOperationRequest { ProductId = movement.ProductId, WarehouseId = movement.WarehouseId,
                    Quantity = movement.Quantity, Note = $"DEMO:{DemoPlanner.DatasetId}; event:{movement.Id:D}; synthetic, no human actor" };
                var response = movement.Type == MovementType.StockIn
                    ? await service.StockInAsync(request, ct) : await service.StockOutAsync(request, ct);
                if (response.QuantityBefore != movement.Before || response.QuantityAfter != movement.After)
                    throw new InvalidOperationException("Movement arithmetic differs from preview.");
            }
            await VerifyDatasetAsync(plan, ct);
            before.VerifyPreserved(await DataSnapshot.CaptureAsync(db, ct), plan);
            db.DemoSeedRuns.Add(new DemoSeedRun { DatasetId = DemoPlanner.DatasetId, PlanHash = plan.Hash,
                PlanJson = plan.Json, CompletedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(false, plan, true);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw; // No retry, restore, repair or deletion.
        }
    }

    public async Task VerifyDatasetAsync(DemoPlan plan, CancellationToken ct = default)
    {
        var productIds = plan.Products.Select(p => p.Id).ToArray();
        var warehouseIds = plan.Warehouses.Select(w => w.Id).ToArray();
        var inventoryIds = plan.Movements.Select(m => m.InventoryId).Distinct().ToArray();
        var movementIds = plan.Movements.Select(m => m.Id).ToArray();
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        var warehouses = await db.Warehouses.IgnoreQueryFilters().AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToListAsync(ct);
        var inventories = await db.Inventories.IgnoreQueryFilters().AsNoTracking().Where(i => inventoryIds.Contains(i.Id)).ToListAsync(ct);
        var movements = await db.StockMovements.IgnoreQueryFilters().AsNoTracking().Where(m => movementIds.Contains(m.Id)).ToListAsync(ct);
        if (products.Count != plan.Products.Length || warehouses.Count != plan.Warehouses.Length
            || inventories.Count != inventoryIds.Length || movements.Count != plan.Movements.Length)
            throw new InvalidOperationException("Dataset incomplete; automatic repair is forbidden.");
        foreach (var p in plan.Products)
        {
            var actual = products.Single(x => x.Id == p.Id);
            if (actual.Barcode != p.Barcode || actual.Name != p.Name || actual.Description != p.Description
                || actual.UnitPrice != p.Price || actual.MinimumStock != p.Minimum || !ValidAudit(actual))
                throw new InvalidOperationException("Demo product changed.");
        }
        foreach (var w in plan.Warehouses)
        {
            var actual = warehouses.Single(x => x.Id == w.Id);
            if (actual.Code != w.Code || actual.Name != w.Name || actual.Location != w.Location || !ValidAudit(actual))
                throw new InvalidOperationException("Demo warehouse changed.");
        }
        foreach (var m in plan.Movements)
        {
            var actual = movements.Single(x => x.Id == m.Id);
            if (actual.InventoryId != m.InventoryId || actual.MovementType != m.Type || actual.Quantity != m.Quantity
                || actual.QuantityBefore != m.Before || actual.QuantityAfter != m.After || !ValidAudit(actual)
                || actual.Note != $"DEMO:{DemoPlanner.DatasetId}; event:{m.Id:D}; synthetic, no human actor")
                throw new InvalidOperationException("Demo movement changed.");
        }
        foreach (var group in plan.Movements.GroupBy(m => m.InventoryId))
        {
            var actual = inventories.Single(i => i.Id == group.Key);
            var last = group.Last();
            if (actual.ProductId != last.ProductId || actual.WarehouseId != last.WarehouseId
                || actual.Quantity != last.After || actual.Version == Guid.Empty || !ValidAudit(actual))
                throw new InvalidOperationException("Demo inventory changed.");
        }
    }

    private static bool ValidAudit(Entities.Common.BaseEntity entity)
        => entity.IsActive && entity.CreatedAtUtc != default && entity.CreatedByUserId is null
            && entity.UpdatedByUserId is null && entity.DeactivatedByUserId is null && entity.DeactivatedAtUtc is null;

    private sealed class TransactionStockRepository(InventoryDbContext context, PlannedMovement planned) : IStockRepository
    {
        private readonly StockRepository reads = new(context);
        public Task<Product?> GetProductByIdAsync(Guid id, CancellationToken ct) => reads.GetProductByIdAsync(id, ct);
        public Task<Warehouse?> GetWarehouseByIdAsync(Guid id, CancellationToken ct) => reads.GetWarehouseByIdAsync(id, ct);
        public Task<Inventory?> GetInventoryByProductAndWarehouseAsync(Guid p, Guid w, CancellationToken ct)
            => reads.GetInventoryByProductAndWarehouseAsync(p, w, ct);
        public async Task SaveStockOperationAsync(Inventory inventory, StockMovement movement, bool isNew, CancellationToken ct)
        {
            if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Outer transaction required.");
            if (isNew) { inventory.Id = planned.InventoryId; context.Inventories.Add(inventory); }
            if (inventory.Id != planned.InventoryId) throw new InvalidOperationException("Cannot touch existing stock.");
            movement.Id = planned.Id;
            movement.InventoryId = inventory.Id;
            context.StockMovements.Add(movement);
            await context.SaveChangesAsync(ct); // Existing audit interceptor and Version concurrency token apply.
        }
        public Task<IReadOnlyList<InventoryStockItemResponse>> GetStockAsync(Guid? p, Guid? w, CancellationToken ct) => reads.GetStockAsync(p, w, ct);
        public Task<IReadOnlyList<StockMovementHistoryResponse>> GetMovementsAsync(Guid? p, Guid? w, int limit, CancellationToken ct) => reads.GetMovementsAsync(p, w, limit, ct);
        public Task<IReadOnlyList<LowStockItemResponse>> GetLowStockAsync(CancellationToken ct) => reads.GetLowStockAsync(ct);
    }
}

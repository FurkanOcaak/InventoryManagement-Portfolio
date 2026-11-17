using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Business.Features.Products.DTOs;
using Business.Features.Products.Validators;
using Business.Features.Warehouses.DTOs;
using Business.Features.Warehouses.Validators;
using DataAccess.Persistence;
using Entities.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.DemoInventory;

public sealed record PlannedProduct(Guid Id, string Barcode, string Name, string Description, decimal Price, int Minimum);
public sealed record PlannedWarehouse(Guid Id, string Code, string Name, string Location);
public sealed record PlannedMovement(Guid Id, Guid InventoryId, Guid ProductId, Guid WarehouseId,
    MovementType Type, int Quantity, int Before, int After);
public sealed record DemoPlan(int ExistingProducts, int ExistingWarehouses, int ExistingMovements,
    PlannedProduct[] Products, PlannedWarehouse[] Warehouses, PlannedMovement[] Movements)
{
    [JsonIgnore] public string Json => JsonSerializer.Serialize(this);
    [JsonIgnore] public string Hash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json)));
}

public static class DemoPlanner
{
    public const string DatasetId = "inventory-demo-j-v1";
    public static Guid Id(string kind, int index)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{DatasetId}:{kind}:{index}"))[..16]);

    public static async Task<DemoPlan> CreateAsync(InventoryDbContext db, CancellationToken ct = default)
    {
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var warehouses = await db.Warehouses.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var movementCount = await db.StockMovements.IgnoreQueryFilters().CountAsync(ct);
        int productCount = Math.Max(0, 40 - products.Count), warehouseCount = Math.Max(0, 5 - warehouses.Count);
        int moves = Math.Max(0, 180 - movementCount);
        // Never borrow stock from existing products to meet an arbitrary count.
        if (productCount == 0 || moves < productCount)
            throw new InvalidOperationException("Targets need manual review: insufficient new products or movements. No writes.");
        var plannedProducts = Enumerable.Range(0, productCount).Select(i =>
        {
            var item = DemoCatalog.Products[i];
            return new PlannedProduct(Id("product", i), $"000-DEMO-J-{i + 1:D4}", item.Name,
                $"Sentetik demo ürünüdür; GS1 tahsisi yoktur. Grup: {item.Group}. Fiyat örnektir (TRY).", item.Price, item.Minimum);
        }).ToArray();
        var newWarehouses = new List<PlannedWarehouse>();
        for (int i = 0; newWarehouses.Count < warehouseCount && i < DemoCatalog.Warehouses.Length; i++)
        {
            var candidate = DemoCatalog.Warehouses[i];
            if (warehouses.Any(w => string.Equals(w.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))) continue;
            newWarehouses.Add(new(Id("warehouse", i), $"DEMO-J-W{i + 1:D2}", candidate.Name, candidate.Location));
        }
        if (newWarehouses.Count != warehouseCount) throw new InvalidOperationException("Warehouse names exhausted.");
        if (plannedProducts.Any(p => products.Any(old => old.Id == p.Id || string.Equals(old.Barcode, p.Barcode, StringComparison.OrdinalIgnoreCase)))
            || newWarehouses.Any(w => warehouses.Any(old => old.Id == w.Id || string.Equals(old.Code, w.Code, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Dataset identity/barcode/code collision. No writes.");
        var destinations = warehouses.Where(w => w.IsActive).OrderBy(w => w.Code, StringComparer.Ordinal)
            .Select(w => w.Id).Concat(newWarehouses.Select(w => w.Id)).ToArray();
        if (destinations.Length == 0) throw new InvalidOperationException("No active warehouse.");
        var result = new List<PlannedMovement>();
        for (int i = 0; i < productCount; i++)
        {
            var product = plannedProducts[i];
            new ProductCreateRequestValidator().ValidateAndThrow(new ProductCreateRequest
            { Barcode = product.Barcode, Name = product.Name, Description = product.Description, UnitPrice = product.Price, MinimumStock = product.Minimum });
            int count = moves / productCount + (i < moves % productCount ? 1 : 0);
            int target = i % 3 == 0 ? Math.Max(1, product.Minimum / 2) : product.Minimum * 6;
            int step = Math.Min(5, target);
            int before = 0;
            for (int j = 0; j < count; j++)
            {
                var type = j == 0 || j % 2 == 0 ? MovementType.StockIn : MovementType.StockOut;
                int quantity = j == 0 ? target + (count % 2 == 0 ? step : 0) : step;
                int after = type == MovementType.StockIn ? checked(before + quantity) : before - quantity;
                if (after < 0) throw new InvalidOperationException("Negative planned stock.");
                result.Add(new(Id("movement", result.Count), Id("inventory", i), product.Id,
                    destinations[i % destinations.Length], type, quantity, before, after));
                before = after;
            }
        }
        foreach (var w in newWarehouses)
            new WarehouseCreateRequestValidator().ValidateAndThrow(new WarehouseCreateRequest { Code = w.Code, Name = w.Name, Location = w.Location });
        var movementIds = result.Select(m => m.Id).ToArray();
        var inventoryIds = result.Select(m => m.InventoryId).Distinct().ToArray();
        if (await db.StockMovements.IgnoreQueryFilters().AnyAsync(m => movementIds.Contains(m.Id), ct)
            || await db.Inventories.IgnoreQueryFilters().AnyAsync(i => inventoryIds.Contains(i.Id), ct))
            throw new InvalidOperationException("Orphan dataset records exist without ledger. No automatic repair.");
        return new(products.Count, warehouses.Count, movementCount, plannedProducts, newWarehouses.ToArray(), result.ToArray());
    }
}

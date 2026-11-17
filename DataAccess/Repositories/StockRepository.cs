
using Business.Features.Stock.Abstractions;
using Business.Features.Stock.DTOs;
using Business.Features.Stock.Exceptions;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Repositories;

public class StockRepository : IStockRepository
{
    private readonly InventoryDbContext _context;

    public StockRepository(InventoryDbContext context)
    {
        _context = context;
    }

    public Task<Product?> GetProductByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return _context.Products.FirstOrDefaultAsync(
            x => x.Id == id && x.IsActive,
            cancellationToken);
    }

    public Task<Warehouse?> GetWarehouseByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return _context.Warehouses.FirstOrDefaultAsync(
            x => x.Id == id && x.IsActive,
            cancellationToken);
    }

    public Task<Inventory?> GetInventoryByProductAndWarehouseAsync(
        Guid productId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        return _context.Inventories.FirstOrDefaultAsync(
            x => x.ProductId == productId &&
                 x.WarehouseId == warehouseId,
            cancellationToken);
    }

    // Stok giriş/çıkış işlemleri
    public async Task SaveStockOperationAsync(
        Inventory inventory,
        StockMovement movement,
        bool isNewInventory,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            if (isNewInventory)
            {
                await _context.Inventories.AddAsync(
                    inventory,
                    cancellationToken);

                await _context.SaveChangesAsync(
                    cancellationToken);
            }

            movement.InventoryId = inventory.Id;

            await _context.StockMovements.AddAsync(
                movement,
                cancellationToken);

            await _context.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new StockConflictException(
                "Stok eşzamanlı güncelleme çakışması yaşandı.",
                ex);
        }
        catch (DbUpdateException ex)
            when (IsInventoryUniqueConstraintViolation(ex))
        {
            throw new StockConflictException(
                "Bu ürün ve depo için stok kaydı zaten mevcut.",
                ex);
        }
    }

    // Güncel stok listesi
    public async Task<IReadOnlyList<InventoryStockItemResponse>> GetStockAsync(
        Guid? productId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        var query =
            from inventory in _context.Inventories.AsNoTracking()
            join product in _context.Products.AsNoTracking()
                on inventory.ProductId equals product.Id
            join warehouse in _context.Warehouses.AsNoTracking()
                on inventory.WarehouseId equals warehouse.Id
            select new { inventory, product, warehouse };

        if (productId.HasValue)
        {
            query = query.Where(x =>
                x.inventory.ProductId == productId.Value);
        }

        if (warehouseId.HasValue)
        {
            query = query.Where(x =>
                x.inventory.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderBy(x => x.product.Name)
            .ThenBy(x => x.warehouse.Name)
            .Select(x => new InventoryStockItemResponse
            {
                InventoryId = x.inventory.Id,
                ProductId = x.product.Id,
                ProductBarcode = x.product.Barcode,
                ProductName = x.product.Name,
                WarehouseId = x.warehouse.Id,
                WarehouseCode = x.warehouse.Code,
                WarehouseName = x.warehouse.Name,
                Quantity = x.inventory.Quantity,
                MinimumStock = x.product.MinimumStock
            })
            .ToListAsync(cancellationToken);
    }

    // Stok hareket geçmişi
    public async Task<IReadOnlyList<StockMovementHistoryResponse>> GetMovementsAsync(
        Guid? productId,
        Guid? warehouseId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query =
            from movement in _context.StockMovements
                .IgnoreQueryFilters().AsNoTracking()
            join inventory in _context.Inventories
                .IgnoreQueryFilters().AsNoTracking()
                on movement.InventoryId equals inventory.Id
            join product in _context.Products
                .IgnoreQueryFilters().AsNoTracking()
                on inventory.ProductId equals product.Id
            join warehouse in _context.Warehouses
                .IgnoreQueryFilters().AsNoTracking()
                on inventory.WarehouseId equals warehouse.Id
            select new { movement, inventory, product, warehouse };

        if (productId.HasValue)
        {
            query = query.Where(x =>
                x.inventory.ProductId == productId.Value);
        }

        if (warehouseId.HasValue)
        {
            query = query.Where(x =>
                x.inventory.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderByDescending(x => x.movement.CreatedAtUtc)
            .ThenByDescending(x => x.movement.Id)
            .Take(limit)
            .Select(x => new StockMovementHistoryResponse
            {
                StockMovementId = x.movement.Id,
                ProductId = x.product.Id,
                ProductBarcode = x.product.Barcode,
                WarehouseId = x.warehouse.Id,
                WarehouseCode = x.warehouse.Code,
                MovementType = x.movement.MovementType,
                Quantity = x.movement.Quantity,
                QuantityBefore = x.movement.QuantityBefore,
                QuantityAfter = x.movement.QuantityAfter,
                Note = x.movement.Note,
                CreatedAtUtc = x.movement.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    // Kritik stok listesi
    public async Task<IReadOnlyList<LowStockItemResponse>> GetLowStockAsync(
        CancellationToken cancellationToken)
    {
        return await (
            from inventory in _context.Inventories.AsNoTracking()
            join product in _context.Products.AsNoTracking()
                on inventory.ProductId equals product.Id
            join warehouse in _context.Warehouses.AsNoTracking()
                on inventory.WarehouseId equals warehouse.Id
            where inventory.Quantity <= product.MinimumStock
            select new LowStockItemResponse
            {
                ProductId = product.Id,
                ProductBarcode = product.Barcode,
                ProductName = product.Name,
                WarehouseId = warehouse.Id,
                WarehouseName = warehouse.Name,
                Quantity = inventory.Quantity,
                MinimumStock = product.MinimumStock,
                Shortage = product.MinimumStock - inventory.Quantity
            })
            .ToListAsync(cancellationToken);
    }

    private static bool IsInventoryUniqueConstraintViolation(
        DbUpdateException exception)
    {
        string message =
            exception.InnerException?.Message ?? string.Empty;

        bool uniqueViolation =
            message.Contains(
                "UNIQUE",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains(
                "duplicate",
                StringComparison.OrdinalIgnoreCase);

        bool inventoryIndex =
            message.Contains(
                "IX_Inventories_ProductId_WarehouseId",
                StringComparison.OrdinalIgnoreCase) ||
            (
                message.Contains(
                    "Inventories.ProductId",
                    StringComparison.OrdinalIgnoreCase) &&
                message.Contains(
                    "Inventories.WarehouseId",
                    StringComparison.OrdinalIgnoreCase)
            );

        return uniqueViolation && inventoryIndex;
    }
}

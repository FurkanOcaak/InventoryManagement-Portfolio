
using Business.Features.Stock.DTOs;
using Entities.Entities;

namespace Business.Features.Stock.Abstractions;

public interface IStockRepository
{
    Task<Product?> GetProductByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<Warehouse?> GetWarehouseByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<Inventory?> GetInventoryByProductAndWarehouseAsync(
        Guid productId,
        Guid warehouseId,
        CancellationToken cancellationToken);

    Task SaveStockOperationAsync(
        Inventory inventory,
        StockMovement movement,
        bool isNewInventory,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryStockItemResponse>> GetStockAsync(
        Guid? productId,
        Guid? warehouseId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockMovementHistoryResponse>> GetMovementsAsync(
        Guid? productId,
        Guid? warehouseId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LowStockItemResponse>> GetLowStockAsync(
        CancellationToken cancellationToken);
}

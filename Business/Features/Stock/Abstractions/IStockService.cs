
using Business.Features.Stock.DTOs;

namespace Business.Features.Stock.Abstractions;

public interface IStockService
{
    Task<StockOperationResponse> StockInAsync(
        StockOperationRequest request,
        CancellationToken cancellationToken);

    Task<StockOperationResponse> StockOutAsync(
        StockOperationRequest request,
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

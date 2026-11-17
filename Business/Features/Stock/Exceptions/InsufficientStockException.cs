namespace Business.Features.Stock.Exceptions;

public sealed class InsufficientStockException : Exception
{
    public InsufficientStockException(Guid productId, Guid warehouseId)
        : base($"Inventory for product '{productId}' and warehouse '{warehouseId}' does not have enough quantity.")
    {
        ProductId = productId;
        WarehouseId = warehouseId;
    }

    public Guid ProductId { get; }

    public Guid WarehouseId { get; }
}

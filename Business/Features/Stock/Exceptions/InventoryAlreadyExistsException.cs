namespace Business.Features.Stock.Exceptions;

public sealed class InventoryAlreadyExistsException : Exception
{
    public InventoryAlreadyExistsException(Guid productId, Guid warehouseId)
        : base($"Inventory already exists for product '{productId}' and warehouse '{warehouseId}'.")
    {
        ProductId = productId;
        WarehouseId = warehouseId;
    }

    public Guid ProductId { get; }

    public Guid WarehouseId { get; }
}

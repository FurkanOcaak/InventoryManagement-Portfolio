namespace Business.Features.Warehouses.Exceptions;

public sealed class WarehouseAlreadyActiveException : Exception
{
    public WarehouseAlreadyActiveException(Guid warehouseId)
        : base("Depo zaten aktif.")
    {
        WarehouseId = warehouseId;
    }

    public Guid WarehouseId { get; }
}

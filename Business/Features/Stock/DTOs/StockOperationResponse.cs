using Entities.Entities;

namespace Business.Features.Stock.DTOs;

public class StockOperationResponse
{
    public Guid StockMovementId { get; set; }

    public Guid ProductId { get; set; }

    public Guid WarehouseId { get; set; }

    public MovementType MovementType { get; set; }

    public int Quantity { get; set; }

    public int QuantityBefore { get; set; }

    public int QuantityAfter { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

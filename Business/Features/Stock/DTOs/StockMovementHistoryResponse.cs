namespace Business.Features.Stock.DTOs;

public class StockMovementHistoryResponse
{
    public Guid StockMovementId { get; set; }

    public Guid ProductId { get; set; }

    public string ProductBarcode { get; set; } = string.Empty;

    public Guid WarehouseId { get; set; }

    public string WarehouseCode { get; set; } = string.Empty;

    public Entities.Entities.MovementType MovementType { get; set; }

    public int Quantity { get; set; }

    public int QuantityBefore { get; set; }

    public int QuantityAfter { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

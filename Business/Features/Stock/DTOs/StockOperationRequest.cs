namespace Business.Features.Stock.DTOs;

public class StockOperationRequest
{
    public Guid ProductId { get; set; }

    public Guid WarehouseId { get; set; }

    public int Quantity { get; set; }

    public string? Note { get; set; }
}

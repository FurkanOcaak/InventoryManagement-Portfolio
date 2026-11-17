namespace Business.Features.Stock.DTOs;

public class LowStockItemResponse
{
    public Guid ProductId { get; set; }

    public string ProductBarcode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public Guid WarehouseId { get; set; }

    public string WarehouseName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int MinimumStock { get; set; }

    public int Shortage { get; set; }
}

using Entities.Common;

namespace Entities.Entities;

public enum MovementType
{
    StockIn = 1,
    StockOut = 2
}

public class StockMovement : BaseEntity
{
    public Guid InventoryId { get; set; }

    public MovementType MovementType { get; set; }

    public int Quantity { get; set; }

    public int QuantityBefore { get; set; }

    public int QuantityAfter { get; set; }

    public string? Note { get; set; }

    public Inventory Inventory { get; set; } = null!;
}

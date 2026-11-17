using Entities.Common;

namespace Entities.Entities;

public class Inventory : BaseEntity
{
    public Guid ProductId { get; set; }

    public Guid WarehouseId { get; set; }

    public int Quantity { get; set; }

    public Guid Version { get; set; }

    public Product Product { get; set; } = null!;

    public Warehouse Warehouse { get; set; } = null!;
}

using Entities.Common;

namespace Entities.Entities;

public class Product : BaseEntity
{
    public string Barcode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal UnitPrice { get; set; }

    public int MinimumStock { get; set; }
}

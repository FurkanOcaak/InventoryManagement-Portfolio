using Entities.Common;

namespace Entities.Entities;

public class Warehouse : BaseEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Location { get; set; }
}

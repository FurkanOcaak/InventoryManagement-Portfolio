namespace Business.Features.Warehouses.DTOs;

public class WarehouseCreateRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Location { get; set; }
}

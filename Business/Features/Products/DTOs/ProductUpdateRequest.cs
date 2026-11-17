namespace Business.Features.Products.DTOs;

public class ProductUpdateRequest
{
    public string Barcode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal UnitPrice { get; set; }

    public int MinimumStock { get; set; }
}

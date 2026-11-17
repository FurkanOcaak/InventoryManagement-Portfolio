namespace Business.Features.Products.DTOs;

public class ProductResponse
{
    public Guid Id { get; set; }

    public string Barcode { get; set; } = string.Empty;

    public string BarcodeFormat => BarcodeAssessment.Assess(Barcode);

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal UnitPrice { get; set; }

    public int MinimumStock { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}

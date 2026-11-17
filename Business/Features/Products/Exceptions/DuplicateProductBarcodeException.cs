
namespace Business.Features.Products.Exceptions;

public sealed class DuplicateProductBarcodeException : Exception
{
    public DuplicateProductBarcodeException(string barcode)
        : base($"Product barcode '{barcode}' already exists.")
    {
        Barcode = barcode;
    }

    public string Barcode { get; }
}

namespace Business.Features.Products.Exceptions;

public sealed class ProductAlreadyActiveException : Exception
{
    public ProductAlreadyActiveException(Guid productId)
        : base("Ürün zaten aktif.")
    {
        ProductId = productId;
    }

    public Guid ProductId { get; }
}

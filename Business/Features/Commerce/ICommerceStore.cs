namespace Business.Features.Commerce;

public interface ICommerceStore
{
    Task<PageResult<CatalogProduct>> CatalogAsync(string? search, int page, int size, CancellationToken ct);
    Task<CartResponse> CartAsync(Guid customerId, CancellationToken ct);
    Task<CartResponse> ChangeCartAsync(Guid customerId, Guid productId, int quantity, Guid version,
        string operation, CancellationToken ct);
    Task<CheckoutResult> CheckoutAsync(Guid customerId, CheckoutRequest request, CancellationToken ct);
    Task<PageResult<OrderSummary>> OrdersAsync(Guid customerId, int page, int size, CancellationToken ct);
    Task<OrderDetail?> OrderAsync(Guid customerId, Guid id, CancellationToken ct);
    Task<PageResult<AdminOrderSummary>> AdminOrdersAsync(int page, int size, CancellationToken ct);
    Task<AdminOrderDetail?> AdminOrderAsync(Guid id, CancellationToken ct);
}

public interface IAdminOrderAccess
{
    Task<bool> IsAdminAsync(CancellationToken ct);
}

namespace Business.Features.Commerce;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record CatalogProduct(Guid Id, string Name, string Barcode, string? Description, decimal UnitPrice);
public sealed record CartLineResponse(Guid ProductId, string Name, string Barcode, int Quantity,
    decimal UnitPrice, decimal Subtotal, bool Available);
public sealed record CartResponse(Guid Version, IReadOnlyList<CartLineResponse> Items, decimal Total);
public sealed record OrderSummary(Guid Id, string Number, DateTime CreatedAtUtc, string Status, decimal Total);
public sealed record OrderLineResponse(Guid ProductId, string Name, string Barcode, int Quantity, decimal UnitPrice, decimal Subtotal);
public sealed record OrderDetail(Guid Id, string Number, DateTime CreatedAtUtc, string Status, decimal Total,
    IReadOnlyList<OrderLineResponse> Items);
public sealed record AdminOrderSummary(OrderSummary Order, string CustomerCode, string CustomerName);
public sealed record AdminOrderDetail(OrderDetail Order, string CustomerCode, string CustomerName);
public sealed record CheckoutResult(OrderDetail Order, bool Replayed);
public sealed record AddCartItemRequest(Guid ProductId, int Quantity, Guid CartVersion);
public sealed record SetCartQuantityRequest(int Quantity, Guid CartVersion);
public sealed record CheckoutRequest(Guid IdempotencyKey, Guid CartVersion);

public enum CommerceFailure { Invalid, Forbidden, Conflict }
public sealed class CommerceException(CommerceFailure failure, string message) : Exception(message)
{
    public CommerceFailure Failure { get; } = failure;
}

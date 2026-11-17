using Business.Common.Security;

namespace Business.Features.Commerce;

// Ownership is resolved here as well as at the API policy boundary. No method accepts a client CustomerId.
public sealed class CommerceService(ICommerceStore store, ICurrentCustomer currentCustomer, IAdminOrderAccess admin)
{
    public const int MaximumQuantity = 10_000;
    public const int MaximumCartLines = 100;
    public const decimal MaximumMoney = 9999999999999999.99m;

    private async Task<Guid> OwnerAsync(CancellationToken ct)
        => await currentCustomer.GetCustomerIdAsync(ct)
            ?? throw new CommerceException(CommerceFailure.Forbidden, "Aktif müşteri hesabı doğrulanamadı.");

    private static void Page(int page, int size)
    {
        if (page is < 1 or > 100_000 || size is < 1 or > 100)
            throw new CommerceException(CommerceFailure.Invalid, "Geçersiz sayfalama: sayfa 1–100000, sayfa boyutu 1–100 olmalı.");
    }

    public async Task<PageResult<CatalogProduct>> CatalogAsync(string? search, int page, int size, CancellationToken ct)
    {
        await OwnerAsync(ct);
        Page(page, size);
        if (search?.Length > 100) throw new CommerceException(CommerceFailure.Invalid, "Arama en fazla 100 karakter olabilir.");
        return await store.CatalogAsync(search?.Trim(), page, size, ct);
    }

    public async Task<CartResponse> CartAsync(CancellationToken ct) => await store.CartAsync(await OwnerAsync(ct), ct);

    public async Task<CartResponse> ChangeCartAsync(Guid productId, int quantity, Guid version, string operation, CancellationToken ct)
    {
        var owner = await OwnerAsync(ct);
        if (productId == Guid.Empty || operation is not ("add" or "set" or "remove")
            || (operation != "remove" && quantity is < 1 or > MaximumQuantity))
            throw new CommerceException(CommerceFailure.Invalid, "Ürün ve 1–10000 arasında tam sayı miktar gerekli.");
        return await store.ChangeCartAsync(owner, productId, quantity, version, operation, ct);
    }

    public async Task<CheckoutResult> CheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var owner = await OwnerAsync(ct);
        if (request.IdempotencyKey == Guid.Empty || request.CartVersion == Guid.Empty)
            throw new CommerceException(CommerceFailure.Invalid, "Sipariş anahtarı ve sepet sürümü gerekli.");
        return await store.CheckoutAsync(owner, request, ct);
    }

    public async Task<PageResult<OrderSummary>> OrdersAsync(int page, int size, CancellationToken ct)
    {
        var owner = await OwnerAsync(ct);
        Page(page, size);
        return await store.OrdersAsync(owner, page, size, ct);
    }

    public async Task<OrderDetail> OrderAsync(Guid id, CancellationToken ct)
        => await store.OrderAsync(await OwnerAsync(ct), id, ct) ?? throw new KeyNotFoundException();

    private async Task RequireAdminAsync(CancellationToken ct)
    {
        if (!await admin.IsAdminAsync(ct)) throw new CommerceException(CommerceFailure.Forbidden, "Sipariş yönetimi yetkiniz yok.");
    }

    public async Task<PageResult<AdminOrderSummary>> AdminOrdersAsync(int page, int size, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        Page(page, size);
        return await store.AdminOrdersAsync(page, size, ct);
    }

    public async Task<AdminOrderDetail> AdminOrderAsync(Guid id, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        return await store.AdminOrderAsync(id, ct) ?? throw new KeyNotFoundException();
    }
}

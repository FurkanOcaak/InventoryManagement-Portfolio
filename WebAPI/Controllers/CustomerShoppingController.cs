using Business.Common.Security;
using Business.Features.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/customer-portal")]
[Authorize(Policy = ApplicationPolicies.CustomerShopping)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerShoppingController(CommerceService service) : ControllerBase
{
    [HttpGet("products")]
    public Task<PageResult<CatalogProduct>> Products(string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => service.CatalogAsync(search, page, pageSize, ct);

    [HttpGet("cart")]
    public Task<CartResponse> Cart(CancellationToken ct) => service.CartAsync(ct);

    [HttpPost("cart/items")]
    public Task<CartResponse> Add(AddCartItemRequest request, CancellationToken ct)
        => service.ChangeCartAsync(request.ProductId, request.Quantity, request.CartVersion, "add", ct);

    [HttpPatch("cart/items/{productId:guid}")]
    public Task<CartResponse> Set(Guid productId, SetCartQuantityRequest request, CancellationToken ct)
        => service.ChangeCartAsync(productId, request.Quantity, request.CartVersion, "set", ct);

    [HttpDelete("cart/items/{productId:guid}")]
    public Task<CartResponse> Remove(Guid productId, [FromQuery] Guid version, CancellationToken ct)
        => service.ChangeCartAsync(productId, 0, version, "remove", ct);

    [HttpPost("orders")]
    public async Task<IActionResult> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        var result = await service.CheckoutAsync(request, ct);
        Response.Headers.Location = $"/api/customer-portal/orders/{result.Order.Id}";
        return StatusCode(result.Replayed ? StatusCodes.Status200OK : StatusCodes.Status201Created, result.Order);
    }

    [HttpGet("orders")]
    public Task<PageResult<OrderSummary>> Orders(int page = 1, int pageSize = 20, CancellationToken ct = default)
        => service.OrdersAsync(page, pageSize, ct);

    [HttpGet("orders/{id:guid}")]
    public Task<OrderDetail> Order(Guid id, CancellationToken ct) => service.OrderAsync(id, ct);
}

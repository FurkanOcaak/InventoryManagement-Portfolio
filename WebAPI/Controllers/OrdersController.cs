using Business.Common.Security;
using Business.Features.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = ApplicationPolicies.OrdersRead)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrdersController(CommerceService service) : ControllerBase
{
    [HttpGet]
    public Task<PageResult<AdminOrderSummary>> List(int page = 1, int pageSize = 20, CancellationToken ct = default)
        => service.AdminOrdersAsync(page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<AdminOrderDetail> Detail(Guid id, CancellationToken ct) => service.AdminOrderAsync(id, ct);
}

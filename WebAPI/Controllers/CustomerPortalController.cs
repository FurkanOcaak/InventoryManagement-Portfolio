using Business.Common.Security;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/customer-portal")]
[Authorize(Policy = ApplicationPolicies.CustomerPortal)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerPortalController(
    ICurrentCustomer currentCustomer, InventoryDbContext context) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var id = await currentCustomer.GetCustomerIdAsync(cancellationToken);
        if (id is null) return Forbid();
        var customer = await context.Customers.AsNoTracking()
            .Where(customer => customer.Id == id)
            .Select(customer => new { customer.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return customer is null ? Forbid() : Ok(customer);
    }
}

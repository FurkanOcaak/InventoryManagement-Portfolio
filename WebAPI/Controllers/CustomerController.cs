using Microsoft.AspNetCore.Mvc;
using Business.Features.Customers.Abstractions;
using Business.Features.Customers.DTOs;

using Microsoft.AspNetCore.Authorization;
using Business.Common.Security;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = ApplicationPolicies.CustomersRead)]
public class CustomerController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomerController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var customers = await _customerService.GetAllAsync(cancellationToken);

        return Ok(customers);
    }

    [HttpGet("inactive")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> GetInactiveAsync(CancellationToken cancellationToken)
    {
        var customers = await _customerService.GetInactiveAsync(cancellationToken);

        return Ok(customers);
    }

    [HttpPatch("{id:guid}/reactivate")]
    [Authorize(Policy = ApplicationPolicies.CustomersManage)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerService.ReactivateAsync(id, cancellationToken);

        return Ok(customer);
    }

    [HttpGet("{id:guid}")]
    [ActionName(nameof(GetByIdAsync))]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerService.GetByIdAsync(id, cancellationToken);

        if (customer is null)
        {
            return NotFound();
        }

        return Ok(customer);
    }

    [HttpPost]
    [Authorize(Policy = ApplicationPolicies.CustomersManage)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerResponse>> CreateAsync(
        [FromBody] CustomerCreateRequest request,
        CancellationToken cancellationToken)
    {
        var createdCustomer = await _customerService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(
            nameof(GetByIdAsync),
            new { id = createdCustomer.Id },
            createdCustomer);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = ApplicationPolicies.CustomersManage)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerResponse>> UpdateAsync(
        Guid id,
        [FromBody] CustomerUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var updatedCustomer = await _customerService.UpdateAsync(id, request, cancellationToken);

        return Ok(updatedCustomer);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = ApplicationPolicies.CustomersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        await _customerService.DeactivateAsync(id, cancellationToken);

        return NoContent();
    }
}

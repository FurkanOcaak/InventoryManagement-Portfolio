using Business.Features.Warehouses.Abstractions;
using Business.Features.Warehouses.DTOs;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;
using Business.Common.Security;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/warehouses")]
[Authorize(Policy = ApplicationPolicies.WarehousesRead)]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;

    public WarehousesController(IWarehouseService warehouseService)
    {
        _warehouseService = warehouseService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WarehouseResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WarehouseResponse>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var warehouses = await _warehouseService.GetAllAsync(cancellationToken);

        return Ok(warehouses);
    }

    [HttpGet("inactive")]
    [Authorize(Policy = ApplicationPolicies.WarehousesManage)]
    [ProducesResponseType(typeof(IReadOnlyList<WarehouseResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WarehouseResponse>>> GetInactiveAsync(CancellationToken cancellationToken)
    {
        var warehouses = await _warehouseService.GetInactiveAsync(cancellationToken);

        return Ok(warehouses);
    }

    [HttpPatch("{id:guid}/reactivate")]
    [Authorize(Policy = ApplicationPolicies.WarehousesManage)]
    [ProducesResponseType(typeof(WarehouseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WarehouseResponse>> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await _warehouseService.ReactivateAsync(id, cancellationToken);

        return Ok(warehouse);
    }

    [HttpGet("{id:guid}")]
    [ActionName(nameof(GetByIdAsync))]
    [ProducesResponseType(typeof(WarehouseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await _warehouseService.GetByIdAsync(id, cancellationToken);

        if (warehouse is null)
        {
            return NotFound();
        }

        return Ok(warehouse);
    }

    [HttpPost]
    [Authorize(Policy = ApplicationPolicies.WarehousesManage)]
    [ProducesResponseType(typeof(WarehouseResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<WarehouseResponse>> CreateAsync(
        [FromBody] WarehouseCreateRequest request,
        CancellationToken cancellationToken)
    {
        var createdWarehouse = await _warehouseService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(
            nameof(GetByIdAsync),
            new { id = createdWarehouse.Id },
            createdWarehouse);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = ApplicationPolicies.WarehousesManage)]
    [ProducesResponseType(typeof(WarehouseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WarehouseResponse>> UpdateAsync(
        Guid id,
        [FromBody] WarehouseUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var updatedWarehouse = await _warehouseService.UpdateAsync(id, request, cancellationToken);

        return Ok(updatedWarehouse);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = ApplicationPolicies.WarehousesManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        await _warehouseService.DeactivateAsync(id, cancellationToken);

        return NoContent();
    }
}

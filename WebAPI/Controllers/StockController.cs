using Business.Features.Stock.Abstractions;
using Business.Features.Stock.DTOs;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Business.Common.Security;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize(Policy = ApplicationPolicies.StockRead)]
public class StockController : ControllerBase
{
    private readonly IStockService _stockService;

    public StockController(IStockService stockService)
    {
        _stockService = stockService;
    }

    [HttpPost("in")]
    [Authorize(Policy = ApplicationPolicies.StockIn)]
    [ProducesResponseType(typeof(StockOperationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockOperationResponse>> StockInAsync(
        [FromBody] StockOperationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.StockInAsync(request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("out")]
    [Authorize(Policy = ApplicationPolicies.StockOut)]
    [ProducesResponseType(typeof(StockOperationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockOperationResponse>> StockOutAsync(
        [FromBody] StockOperationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.StockOutAsync(request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InventoryStockItemResponse>>> GetStockAsync(
        [FromQuery] Guid? productId,
        [FromQuery] Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.GetStockAsync(
            productId,
            warehouseId,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("movements")]
    public async Task<ActionResult<IReadOnlyList<StockMovementHistoryResponse>>> GetMovementsAsync(
        [FromQuery] Guid? productId,
        [FromQuery] Guid? warehouseId,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _stockService.GetMovementsAsync(
            productId,
            warehouseId,
            limit,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<LowStockItemResponse>>> GetLowStockAsync(
        CancellationToken cancellationToken)
    {
        var result = await _stockService.GetLowStockAsync(
            cancellationToken);

        return Ok(result);
    }

}

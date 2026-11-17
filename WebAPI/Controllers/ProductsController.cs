using Business.Features.Products.Abstractions;
using Business.Features.Products.DTOs;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;
using Business.Common.Security;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Policy = ApplicationPolicies.ProductsRead)]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var products = await _productService.GetAllAsync(cancellationToken);

        return Ok(products);
    }

    [HttpGet("inactive")]
    [Authorize(Policy = ApplicationPolicies.ProductsManage)]
    [ProducesResponseType(typeof(IReadOnlyList<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetInactiveAsync(CancellationToken cancellationToken)
    {
        var products = await _productService.GetInactiveAsync(cancellationToken);

        return Ok(products);
    }

    [HttpPatch("{id:guid}/reactivate")]
    [Authorize(Policy = ApplicationPolicies.ProductsManage)]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productService.ReactivateAsync(id, cancellationToken);

        return Ok(product);
    }

    [HttpGet("{id:guid}")]
    [ActionName(nameof(GetByIdAsync))]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        return Ok(product);
    }

    [HttpPost]
    [Authorize(Policy = ApplicationPolicies.ProductsManage)]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductResponse>> CreateAsync(
        [FromBody] ProductCreateRequest request,
        CancellationToken cancellationToken)
    {
        var createdProduct = await _productService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(
            nameof(GetByIdAsync),
            new { id = createdProduct.Id },
            createdProduct);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = ApplicationPolicies.ProductsManage)]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductResponse>> UpdateAsync(
        Guid id,
        [FromBody] ProductUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var updatedProduct = await _productService.UpdateAsync(id, request, cancellationToken);

        return Ok(updatedProduct);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = ApplicationPolicies.ProductsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        await _productService.DeactivateAsync(id, cancellationToken);

        return NoContent();
    }
}

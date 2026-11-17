using Business.Features.Products.Abstractions;
using Business.Features.Products.DTOs;
using Business.Features.Products.Exceptions;
using Entities.Entities;
using FluentValidation;

namespace Business.Features.Products.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IValidator<ProductCreateRequest> _createValidator;
    private readonly IValidator<ProductUpdateRequest> _updateValidator;

    public ProductService(
        IProductRepository productRepository,
        IValidator<ProductCreateRequest> createValidator,
        IValidator<ProductUpdateRequest> updateValidator)
    {
        _productRepository = productRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _productRepository.GetByIdAsync(id, cancellationToken);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var entities = await _productRepository.GetAllAsync(cancellationToken);

        return entities
            .Select(ToResponse)
            .ToList();
    }

    public async Task<IReadOnlyList<ProductResponse>> GetInactiveAsync(CancellationToken cancellationToken)
    {
        var entities = await _productRepository.GetInactiveAsync(cancellationToken);

        return entities.Select(ToResponse).ToList();
    }

    public async Task<ProductResponse> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdIncludingInactiveAsync(id, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Yeniden aktifleştirilecek ürün bulunamadı.");
        }

        if (product.IsActive)
        {
            throw new ProductAlreadyActiveException(id);
        }

        product.IsActive = true;
        product.DeactivatedAtUtc = null;
        product.DeactivatedByUserId = null;

        await _productRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(product);
    }

    public async Task<ProductResponse> CreateAsync(ProductCreateRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var exists = await _productRepository.ExistsByBarcodeAsync(request.Barcode, cancellationToken);

        if (exists)
        {
            throw new DuplicateProductBarcodeException(request.Barcode);
        }

        var product = new Product
        {
            Barcode = request.Barcode,
            Name = request.Name,
            Description = request.Description,
            UnitPrice = request.UnitPrice,
            MinimumStock = request.MinimumStock,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = null,
            DeactivatedAtUtc = null,
            CreatedByUserId = null,
            UpdatedByUserId = null,
            DeactivatedByUserId = null,
        };

        await _productRepository.AddAsync(product, cancellationToken);
        await _productRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(product);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, ProductUpdateRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _updateValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var existingProduct = await _productRepository.GetByIdAsync(id, cancellationToken);

        if (existingProduct is null)
        {
            throw new KeyNotFoundException("Güncellenecek ürün bulunamadı.");
        }

        var barcodeChanged = !string.Equals(existingProduct.Barcode, request.Barcode, StringComparison.Ordinal);

        if (barcodeChanged)
        {
            var exists = await _productRepository.ExistsByBarcodeAsync(request.Barcode, cancellationToken);

            if (exists)
            {
                throw new DuplicateProductBarcodeException(request.Barcode);
            }
        }

        existingProduct.Barcode = request.Barcode;
        existingProduct.Name = request.Name;
        existingProduct.Description = request.Description;
        existingProduct.UnitPrice = request.UnitPrice;
        existingProduct.MinimumStock = request.MinimumStock;

        await _productRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(existingProduct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Pasifleştirilecek ürün bulunamadı.");
        }

        _productRepository.Remove(product);
        await _productRepository.SaveChangesAsync(cancellationToken);
    }

    private static ProductResponse ToResponse(Product product)
    {
        return new ProductResponse
        {
            Id = product.Id,
            Barcode = product.Barcode,
            Name = product.Name,
            Description = product.Description,
            UnitPrice = product.UnitPrice,
            MinimumStock = product.MinimumStock,
            IsActive = product.IsActive,
            CreatedAtUtc = product.CreatedAtUtc,
            UpdatedAtUtc = product.UpdatedAtUtc
        };
    }
}

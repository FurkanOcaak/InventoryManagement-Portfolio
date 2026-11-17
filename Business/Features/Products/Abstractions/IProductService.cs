using Business.Features.Products.DTOs;

namespace Business.Features.Products.Abstractions;

public interface IProductService
{
    Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductResponse>> GetInactiveAsync(CancellationToken cancellationToken);

    Task<ProductResponse> ReactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductResponse> CreateAsync(ProductCreateRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> UpdateAsync(Guid id, ProductUpdateRequest request, CancellationToken cancellationToken);

    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}

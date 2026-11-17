using Entities.Entities;

namespace Business.Features.Products.Abstractions;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetInactiveAsync(CancellationToken cancellationToken);

    Task<Product?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByBarcodeAsync(string barcode, CancellationToken cancellationToken);

    Task AddAsync(Product product, CancellationToken cancellationToken);

    void Remove(Product product);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

using Entities.Entities;

namespace Business.Features.Warehouses.Abstractions;

public interface IWarehouseRepository
{
    Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Warehouse>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Warehouse>> GetInactiveAsync(CancellationToken cancellationToken);

    Task<Warehouse?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken);

    Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken);

    void Remove(Warehouse warehouse);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

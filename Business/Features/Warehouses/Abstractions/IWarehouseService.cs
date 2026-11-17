using Business.Features.Warehouses.DTOs;

namespace Business.Features.Warehouses.Abstractions;

public interface IWarehouseService
{
    Task<WarehouseResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<WarehouseResponse>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<WarehouseResponse>> GetInactiveAsync(CancellationToken cancellationToken);

    Task<WarehouseResponse> ReactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<WarehouseResponse> CreateAsync(WarehouseCreateRequest request, CancellationToken cancellationToken);

    Task<WarehouseResponse> UpdateAsync(Guid id, WarehouseUpdateRequest request, CancellationToken cancellationToken);

    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}

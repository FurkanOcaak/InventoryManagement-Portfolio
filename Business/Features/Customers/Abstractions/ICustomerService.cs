using Business.Features.Customers.DTOs;

namespace Business.Features.Customers.Abstractions;

public interface ICustomerService
{
    Task<CustomerResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerResponse>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerResponse>> GetInactiveAsync(CancellationToken cancellationToken);

    Task<CustomerResponse> ReactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerResponse> CreateAsync(CustomerCreateRequest request, CancellationToken cancellationToken);

    Task<CustomerResponse> UpdateAsync(Guid id, CustomerUpdateRequest request, CancellationToken cancellationToken);

    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}

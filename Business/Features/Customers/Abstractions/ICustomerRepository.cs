using Entities.Entities;

namespace Business.Features.Customers.Abstractions;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Customer>> GetInactiveAsync(CancellationToken cancellationToken);

    Task<Customer?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    void Remove(Customer customer);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

namespace Business.Common.Security;

// No caller-supplied CustomerId: resolve ownership from the authenticated user.
public interface ICurrentCustomer
{
    Task<Guid?> GetCustomerIdAsync(CancellationToken cancellationToken = default);
}

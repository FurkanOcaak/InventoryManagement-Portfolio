namespace Business.Features.Customers.Exceptions;

public sealed class CustomerAlreadyActiveException : Exception
{
    public CustomerAlreadyActiveException(Guid customerId)
        : base("Müşteri zaten aktif.")
    {
        CustomerId = customerId;
    }

    public Guid CustomerId { get; }
}

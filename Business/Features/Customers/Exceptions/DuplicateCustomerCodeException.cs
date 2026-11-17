namespace Business.Features.Customers.Exceptions;

public sealed class DuplicateCustomerCodeException : Exception
{
    public DuplicateCustomerCodeException(string code)
        : base($"MÃ¼ÅŸteri kodu '{code}' zaten kullanÄ±lÄ±yor.")
    {
        Code = code;
    }

    public string Code { get; }
}

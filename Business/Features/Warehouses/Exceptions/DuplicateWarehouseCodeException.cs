namespace Business.Features.Warehouses.Exceptions;

public sealed class DuplicateWarehouseCodeException : Exception
{
    public DuplicateWarehouseCodeException(string code)
        : base($"Warehouse code '{code}' already exists.")
    {
        Code = code;
    }

    public string Code { get; }
}

namespace Business.Features.Stock.Exceptions;

public sealed class StockConflictException : Exception
{
    public StockConflictException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

using Entities.Common;

namespace Entities.Entities;

public class Order : BaseEntity
{
    public Guid CustomerId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public Guid IdempotencyKey { get; set; }
    public Guid CartVersion { get; set; }
    public decimal Total { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductBarcode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

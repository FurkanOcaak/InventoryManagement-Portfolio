using Entities.Common;

namespace Entities.Entities;

public class ShoppingCart : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<CartItem> Items { get; set; } = [];
}

// Temporary cart lines are hard-deleted; actor/time attribution lives on the cart root.
public class CartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

using System.Data;
using System.Data.Common;
using Business.Common.Security;
using Business.Features.Commerce;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Repositories;

public sealed class CurrentAdminOrderAccess(InventoryDbContext db, ICurrentUser current) : IAdminOrderAccess
{
    public Task<bool> IsAdminAsync(CancellationToken ct) => (
        from user in db.Users
        join membership in db.UserRoles on user.Id equals membership.UserId
        join role in db.Roles on membership.RoleId equals role.Id
        where user.Id == current.UserId && role.Name == ApplicationRoles.Admin && role.NormalizedName == "ADMIN"
        select user.Id).AnyAsync(ct);
}

public sealed class CommerceStore(InventoryDbContext db) : ICommerceStore
{
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAsync(CancellationToken ct)
    {
        try { return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); }
        catch (DbException) { throw Conflict("Başka bir işlem sürüyor. Güncel sepeti yükleyip tekrar deneyin."); }
    }
    private static CommerceException Conflict(string text = "Sepet başka bir işlemle değişti. Güncel sepeti yükleyip tekrar deneyin.")
        => new(CommerceFailure.Conflict, text);

    private static decimal Money(decimal amount)
    {
        if (amount < 0 || amount > CommerceService.MaximumMoney || decimal.Round(amount, 2) != amount)
            throw new CommerceException(CommerceFailure.Invalid, "Fiyat veya toplam izin verilen para sınırını aşıyor.");
        return amount;
    }

    public async Task<PageResult<CatalogProduct>> CatalogAsync(string? search, int page, int size, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().Where(product => product.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(product => product.Name.Contains(search) || product.Barcode.Contains(search));
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(product => new CatalogProduct(product.Id, product.Name, product.Barcode, product.Description, product.UnitPrice)).ToListAsync(ct);
        return new(items, page, size, count);
    }

    public async Task<CartResponse> CartAsync(Guid customerId, CancellationToken ct)
    {
        var cart = await db.ShoppingCarts.AsNoTracking().Include(cart => cart.Items)
            .SingleOrDefaultAsync(cart => cart.CustomerId == customerId, ct);
        return cart is null ? new(Guid.Empty, [], 0) : await CartResponseAsync(cart, ct);
    }

    private async Task<CartResponse> CartResponseAsync(ShoppingCart cart, CancellationToken ct)
    {
        var ids = cart.Items.Select(item => item.ProductId).ToArray();
        // Inactive products remain visible/removable, but cannot be bought.
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(product => ids.Contains(product.Id)).ToDictionaryAsync(product => product.Id, ct);
        var items = cart.Items.OrderBy(item => item.ProductId).Select(item =>
        {
            var product = products[item.ProductId];
            var price = Money(product.UnitPrice);
            return new CartLineResponse(product.Id, product.Name, product.Barcode, item.Quantity,
                price, Money(price * item.Quantity), product.IsActive);
        }).ToList();
        return new(cart.Version, items, Money(items.Sum(item => item.Subtotal)));
    }

    public async Task<CartResponse> ChangeCartAsync(Guid customerId, Guid productId, int quantity, Guid version, string operation, CancellationToken ct)
    {
        await using var transaction = await BeginAsync(ct);
        try
        {
            var cart = await db.ShoppingCarts.Include(cart => cart.Items).SingleOrDefaultAsync(cart => cart.CustomerId == customerId, ct);
            if ((cart?.Version ?? Guid.Empty) != version) throw Conflict();
            if (cart is null)
            {
                if (operation != "add") throw new KeyNotFoundException();
                cart = new ShoppingCart { CustomerId = customerId };
                db.ShoppingCarts.Add(cart);
            }
            var item = cart.Items.SingleOrDefault(item => item.ProductId == productId);
            if (operation == "remove")
            {
                if (item is null) throw new KeyNotFoundException();
                db.CartItems.Remove(item);
                cart.Items.Remove(item);
            }
            else
            {
                if (!await db.Products.AnyAsync(product => product.Id == productId && product.IsActive, ct))
                    throw new CommerceException(CommerceFailure.Invalid, "Aktif ürün bulunamadı.");
                if (operation == "set" && item is null) throw new KeyNotFoundException();
                var next = operation == "add" ? checked((item?.Quantity ?? 0) + quantity) : quantity;
                if (next is < 1 or > CommerceService.MaximumQuantity)
                    throw new CommerceException(CommerceFailure.Invalid, "Ürün miktarı 1–10000 arasında olmalı.");
                if (item is null)
                {
                    if (cart.Items.Count >= CommerceService.MaximumCartLines)
                        throw new CommerceException(CommerceFailure.Invalid, "Sepete en fazla 100 farklı ürün eklenebilir.");
                    var newItem = new CartItem { CartId = cart.Id, ProductId = productId, Quantity = next };
                    cart.Items.Add(newItem);
                    db.CartItems.Add(newItem);
                }
                else item.Quantity = next;
            }
            cart.Version = Guid.NewGuid();
            var response = await CartResponseAsync(cart, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return response;
        }
        catch (Exception error) when (error is DbUpdateException or DbException or OverflowException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw Conflict();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<CheckoutResult> CheckoutAsync(Guid customerId, CheckoutRequest request, CancellationToken ct)
    {
        await using var transaction = await BeginAsync(ct);
        try
        {
            var replay = await db.Orders.AsNoTracking().Include(order => order.Items)
                .SingleOrDefaultAsync(order => order.CustomerId == customerId && order.IdempotencyKey == request.IdempotencyKey, ct);
            if (replay is not null)
            {
                if (replay.CartVersion != request.CartVersion) throw Conflict("Bu sipariş anahtarı farklı bir sepet için kullanılmış.");
                await transaction.CommitAsync(ct);
                return new(Detail(replay), true);
            }
            var cart = await db.ShoppingCarts.Include(cart => cart.Items).SingleOrDefaultAsync(cart => cart.CustomerId == customerId, ct);
            if (cart is null || cart.Version != request.CartVersion) throw Conflict();
            if (cart.Items.Count == 0) throw new CommerceException(CommerceFailure.Invalid, "Boş sepetten sipariş oluşturulamaz.");
            var ids = cart.Items.Select(item => item.ProductId).ToArray();
            var products = await db.Products.AsNoTracking().Where(product => ids.Contains(product.Id) && product.IsActive).ToDictionaryAsync(product => product.Id, ct);
            if (products.Count != cart.Items.Count) throw Conflict("Sepette pasif veya kullanılamayan ürün var. Sepeti güncelleyin.");
            var stock = await (from inventory in db.Inventories.AsNoTracking()
                join warehouse in db.Warehouses.AsNoTracking() on inventory.WarehouseId equals warehouse.Id
                where ids.Contains(inventory.ProductId) && warehouse.IsActive
                select new { inventory.ProductId, inventory.Quantity }).ToListAsync(ct);
            if (stock.Any(row => row.Quantity < 0)) throw Conflict("Stok bilgisi doğrulanamadı.");
            var order = new Order { CustomerId = customerId, Number = "ORD-" + Guid.NewGuid().ToString("N"),
                IdempotencyKey = request.IdempotencyKey, CartVersion = request.CartVersion };
            foreach (var line in cart.Items)
            {
                if (line.Quantity is < 1 or > CommerceService.MaximumQuantity)
                    throw new CommerceException(CommerceFailure.Invalid, "Geçersiz sepet miktarı.");
                var product = products[line.ProductId];
                var available = stock.Where(row => row.ProductId == line.ProductId).Sum(row => (long)row.Quantity);
                if (available < line.Quantity) throw Conflict($"'{product.Name}' için yeterli güncel stok bulunmuyor.");
                var price = Money(product.UnitPrice);
                order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, ProductBarcode = product.Barcode,
                    Quantity = line.Quantity, UnitPrice = price, Subtotal = Money(price * line.Quantity) });
            }
            order.Total = Money(order.Items.Sum(item => item.Subtotal));
            db.Orders.Add(order);
            // Two saves intentionally share one transaction: later failure restores both the order and cart.
            await db.SaveChangesAsync(ct);
            db.CartItems.RemoveRange(cart.Items);
            cart.Items.Clear();
            cart.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(Detail(order), false);
        }
        catch (Exception error) when (error is DbUpdateException or DbException or OverflowException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            // A concurrent commit may already have recorded this same request. Never repeat a write.
            var replay = await db.Orders.AsNoTracking().Include(order => order.Items)
                .SingleOrDefaultAsync(order => order.CustomerId == customerId && order.IdempotencyKey == request.IdempotencyKey, ct);
            if (replay is not null && replay.CartVersion == request.CartVersion) return new(Detail(replay), true);
            throw Conflict("Sipariş eşzamanlı bir işlemle çakıştı. Aynı istek anahtarıyla sonucu kontrol edin.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static OrderSummary Summary(Order order) => new(order.Id, order.Number, DateTime.SpecifyKind(order.CreatedAtUtc, DateTimeKind.Utc), order.Status, order.Total);
    private static OrderDetail Detail(Order order) => new(order.Id, order.Number, DateTime.SpecifyKind(order.CreatedAtUtc, DateTimeKind.Utc), order.Status, order.Total,
        order.Items.OrderBy(item => item.ProductId).Select(item => new OrderLineResponse(item.ProductId, item.ProductName, item.ProductBarcode,
            item.Quantity, item.UnitPrice, item.Subtotal)).ToList());

    public async Task<PageResult<OrderSummary>> OrdersAsync(Guid customerId, int page, int size, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(order => order.CustomerId == customerId);
        var count = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(order => order.CreatedAtUtc).ThenByDescending(order => order.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(orders.Select(Summary).ToList(), page, size, count);
    }

    public async Task<OrderDetail?> OrderAsync(Guid customerId, Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(order => order.Items).SingleOrDefaultAsync(order => order.Id == id && order.CustomerId == customerId, ct);
        return order is null ? null : Detail(order);
    }

    public async Task<PageResult<AdminOrderSummary>> AdminOrdersAsync(int page, int size, CancellationToken ct)
    {
        var query = from order in db.Orders.AsNoTracking()
            join customer in db.Customers.IgnoreQueryFilters().AsNoTracking() on order.CustomerId equals customer.Id
            select new { order, customer.Code, customer.Name };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(row => row.order.CreatedAtUtc).ThenByDescending(row => row.order.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(rows.Select(row => new AdminOrderSummary(Summary(row.order), row.Code, row.Name)).ToList(), page, size, count);
    }

    public async Task<AdminOrderDetail?> AdminOrderAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(order => order.Items).SingleOrDefaultAsync(order => order.Id == id, ct);
        if (order is null) return null;
        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking().SingleAsync(customer => customer.Id == order.CustomerId, ct);
        return new(Detail(order), customer.Code, customer.Name);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Business.Common.Security;
using Business.Features.Commerce;
using DataAccess.Persistence;
using DataAccess.Interceptors;
using Entities.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.Integration;

public class ShoppingApiTests : IDisposable
{
    private readonly ShoppingFactory factory = new();
    private const string Root = "/api/customer-portal";
    private sealed record Session(HttpClient Client, Guid CustomerId, Guid UserId);

    private async Task<Session> CustomerAsync()
    {
        var client = await factory.CreateRoleClientAsync(ApplicationRoles.Customer, freshSession: true);
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        var userId = me.RootElement.GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        var customer = new Customer { Code = "SHOP-" + Guid.NewGuid().ToString("N")[..12], Name = "Shop customer" };
        db.Add(customer);
        (await db.Users.SingleAsync(user => user.Id == userId)).CustomerId = customer.Id;
        await db.SaveChangesAsync();
        return new(client, customer.Id, userId);
    }

    private async Task<Guid> ProductAsync(decimal price = 12.50m, int stock = 100, bool active = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var product = new Product { Barcode = "00" + suffix, Name = "Shop " + suffix, UnitPrice = price, IsActive = active };
        var warehouse = new Warehouse { Code = suffix, Name = "Shop warehouse" };
        db.AddRange(product, warehouse);
        db.Add(new Inventory { Product = product, Warehouse = warehouse, Quantity = stock, Version = Guid.NewGuid() });
        await db.SaveChangesAsync();
        return product.Id;
    }

    private static async Task<CartResponse> CartAsync(HttpClient client)
        => (await client.GetFromJsonAsync<CartResponse>(Root + "/cart"))!;
    private static async Task<CartResponse> AddAsync(HttpClient client, Guid productId, int quantity = 1)
    {
        var current = await CartAsync(client);
        var response = await client.PostAsJsonAsync(Root + "/cart/items", new { productId, quantity, cartVersion = current.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CartResponse>())!;
    }
    private static Task<HttpResponseMessage> CheckoutAsync(HttpClient client, CartResponse cart, Guid? key = null)
        => client.PostAsJsonAsync(Root + "/orders", new { idempotencyKey = key ?? Guid.NewGuid(), cartVersion = cart.Version });

    [Theory]
    [InlineData(ApplicationRoles.Admin)]
    [InlineData(ApplicationRoles.WarehouseStaff)]
    [InlineData(ApplicationRoles.Viewer)]
    public async Task OtherRoles_CannotUseCustomerShopping(string role)
    {
        using var client = await factory.CreateRoleClientAsync(role);
        foreach (var path in new[] { "/products", "/cart", "/orders", "/orders/" + Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Root + path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root + "/cart/items", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root + "/orders", new { })).StatusCode);
    }

    [Theory]
    [InlineData("products")]
    [InlineData("cart")]
    [InlineData("orders")]
    public async Task AnonymousShopping_Is401(string route)
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Root + "/" + route)).StatusCode);
    }

    [Fact]
    public async Task Catalog_IsPagedSearchableAndOnlyExposesActivePublicFields()
    {
        var customer = await CustomerAsync();
        await ProductAsync(); await ProductAsync(); await ProductAsync(active: false);
        using var response = await customer.Client.GetAsync(Root + "/products?search=Shop&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "barcode", "description", "id", "name", "unitPrice" }, item.EnumerateObject().Select(property => property.Name).Order());
        var second = await customer.Client.GetFromJsonAsync<PageResult<CatalogProduct>>(Root + "/products?page=2&pageSize=1");
        Assert.NotEqual(item.GetProperty("id").GetGuid(), Assert.Single(second!.Items).Id);
        Assert.Empty((await customer.Client.GetFromJsonAsync<PageResult<CatalogProduct>>(Root + "/products?search=NOTFOUND"))!.Items);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647")]
    public async Task InvalidPagination_Is400(string query)
    {
        var customer = await CustomerAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.GetAsync(Root + "/products?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.GetAsync(Root + "/orders?" + query)).StatusCode);
    }

    [Fact]
    public async Task Cart_CRUD_DeduplicatesProductsAndKeepsServerPricesAndAudit()
    {
        var customer = await CustomerAsync();
        var productId = await ProductAsync();
        Assert.Empty((await CartAsync(customer.Client)).Items);
        var cart = await AddAsync(customer.Client, productId, 2);
        Assert.Equal(25m, cart.Total);
        cart = await AddAsync(customer.Client, productId, 3);
        Assert.Equal(5, Assert.Single(cart.Items).Quantity);
        var response = await customer.Client.PatchAsJsonAsync(Root + "/cart/items/" + productId,
            new { quantity = 4, cartVersion = cart.Version, unitPrice = 0.01, customerId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        cart = (await response.Content.ReadFromJsonAsync<CartResponse>())!;
        Assert.Equal(50m, cart.Total);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var stored = await db.ShoppingCarts.SingleAsync();
            Assert.Equal(customer.CustomerId, stored.CustomerId);
            Assert.Equal(customer.UserId, stored.CreatedByUserId);
            Assert.Equal(customer.UserId, stored.UpdatedByUserId);
            Assert.Single(await db.CartItems.ToListAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await customer.Client.DeleteAsync(Root + $"/cart/items/{productId}?version={cart.Version}")).StatusCode);
        Assert.Empty((await CartAsync(customer.Client)).Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    [InlineData(int.MaxValue)]
    public async Task InvalidQuantities_Are400WithoutChangingCart(int quantity)
    {
        var customer = await CustomerAsync();
        var productId = await ProductAsync();
        var cart = await AddAsync(customer.Client, productId);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Patch })
        {
            using var request = new HttpRequestMessage(method, Root + "/cart/items" + (method == HttpMethod.Patch ? "/" + productId : ""))
            { Content = JsonContent.Create(new { productId, quantity, cartVersion = cart.Version }) };
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.SendAsync(request)).StatusCode);
        }
        Assert.Equal(1, Assert.Single((await CartAsync(customer.Client)).Items).Quantity);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task NonIntegerOrOverflowJsonQuantity_Is400(string quantity)
    {
        var customer = await CustomerAsync();
        var product = await ProductAsync();
        using var content = new StringContent($$"""{"productId":"{{product}}","quantity":{{quantity}},"cartVersion":"{{Guid.Empty}}"}""", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.PostAsync(Root + "/cart/items", content)).StatusCode);
    }

    [Fact]
    public async Task QuantityAccumulationCannotOverflowOrExceedLimit()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync();
        var cart = await AddAsync(customer.Client, product, 10000);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.PostAsJsonAsync(Root + "/cart/items",
            new { productId = product, quantity = 1, cartVersion = cart.Version })).StatusCode);
        Assert.Equal(10000, Assert.Single((await CartAsync(customer.Client)).Items).Quantity);
    }

    [Fact]
    public async Task CartOwnership_ComesFromSessionAndSurvivesNewLogin()
    {
        var first = await CustomerAsync(); var second = await CustomerAsync(); var product = await ProductAsync();
        var cart = await AddAsync(first.Client, product, 2);
        Assert.Empty((await second.Client.GetFromJsonAsync<CartResponse>(Root + "/cart?customerId=" + first.CustomerId))!.Items);
        Assert.Equal(HttpStatusCode.Conflict, (await second.Client.DeleteAsync(Root + $"/cart/items/{product}?version={cart.Version}&customerId={first.CustomerId}")).StatusCode);
        Assert.Equal(2, Assert.Single((await CartAsync(first.Client)).Items).Quantity);
        // Fresh HTTP client with the same real login cookie, no in-memory cart state.
        using var freshClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        freshClient.DefaultRequestHeaders.Add("Cookie", first.Client.DefaultRequestHeaders.GetValues("Cookie"));
        Assert.Equal(cart.Version, (await CartAsync(freshClient)).Version);
    }

    [Fact]
    public async Task InactiveProducts_CannotBeAddedOrOrderedButCanBeRemoved()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            (await db.Products.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.False(Assert.Single((await CartAsync(customer.Client)).Items).Available);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.PostAsJsonAsync(Root + "/cart/items", new { productId = product, quantity = 1, cartVersion = cart.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(customer.Client, cart)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.Client.DeleteAsync(Root + $"/cart/items/{product}?version={cart.Version}")).StatusCode);
    }

    [Fact]
    public async Task Checkout_SnapshotsCurrentPricesClearsCartAndNeverChangesStock()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product, 3);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            (await db.Products.SingleAsync()).UnitPrice = 20m;
            await db.SaveChangesAsync();
        }
        var response = await customer.Client.PostAsJsonAsync(Root + "/orders", new { idempotencyKey = Guid.NewGuid(), cartVersion = cart.Version,
            unitPrice = 0, total = 0, customerId = Guid.NewGuid(), status = "Paid" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderDetail>())!;
        Assert.StartsWith("ORD-", order.Number); Assert.Equal("Pending", order.Status); Assert.Equal(60m, order.Total);
        Assert.Equal(20m, Assert.Single(order.Items).UnitPrice);
        Assert.Empty((await CartAsync(customer.Client)).Items);
        using var verification = factory.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(100, (await context.Inventories.SingleAsync()).Quantity);
        Assert.Empty(await context.StockMovements.ToListAsync());
        var saved = await context.Orders.SingleAsync();
        Assert.Equal(customer.CustomerId, saved.CustomerId);
        Assert.Equal(customer.UserId, saved.CreatedByUserId);
        Assert.True(saved.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-5));
        var p = await context.Products.SingleAsync(); p.UnitPrice = 99m; p.Name = "Renamed"; p.Barcode = "CHANGED";
        await context.SaveChangesAsync();
        var snapshot = (await customer.Client.GetFromJsonAsync<OrderDetail>(Root + "/orders/" + order.Id))!;
        Assert.Equal(DateTimeKind.Utc, snapshot.CreatedAtUtc.Kind);
        var history = (await customer.Client.GetFromJsonAsync<PageResult<OrderSummary>>(Root + "/orders"))!;
        Assert.Equal(DateTimeKind.Utc, Assert.Single(history.Items).CreatedAtUtc.Kind);
        Assert.Equal(60m, snapshot.Total); Assert.NotEqual("Renamed", snapshot.Items[0].Name); Assert.NotEqual("CHANGED", snapshot.Items[0].Barcode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task InsufficientStock_RejectsWithoutOrderOrCartCleanup(int stock)
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(stock: stock); var cart = await AddAsync(customer.Client, product, 2);
        Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(customer.Client, cart)).StatusCode);
        Assert.Equal(2, Assert.Single((await CartAsync(customer.Client)).Items).Quantity);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Orders.ToListAsync());
    }

    [Fact]
    public async Task InactiveWarehouseStock_IsNotAvailableForCheckout()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            (await db.Warehouses.SingleAsync()).IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(customer.Client, cart)).StatusCode);
    }

    [Fact]
    public async Task MoneyOverflow_Is400AndDoesNotPersistCartOrOrder()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(CommerceService.MaximumMoney);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.PostAsJsonAsync(Root + "/cart/items",
            new { productId = product, quantity = 2, cartVersion = Guid.Empty })).StatusCode);
        Assert.Empty((await CartAsync(customer.Client)).Items);
    }

    [Fact]
    public async Task Idempotency_ReplaysOriginalOrderWithoutClearingLaterCart()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product);
        var key = Guid.NewGuid();
        var created = await CheckoutAsync(customer.Client, cart, key);
        var original = (await created.Content.ReadFromJsonAsync<OrderDetail>())!;
        var nextCart = await AddAsync(customer.Client, product, 2);
        var repeated = await CheckoutAsync(customer.Client, cart, key);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(original.Id, (await repeated.Content.ReadFromJsonAsync<OrderDetail>())!.Id);
        Assert.Equal(2, Assert.Single((await CartAsync(customer.Client)).Items).Quantity);
        Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(customer.Client, nextCart, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(customer.Client, cart)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Orders.ToListAsync());
    }

    [Fact]
    public async Task OtherCustomersOrder_IsIndistinguishableFromMissingOrder_AdminCanReadAll()
    {
        var first = await CustomerAsync(); var second = await CustomerAsync(); var product = await ProductAsync();
        var firstOrder = (await (await CheckoutAsync(first.Client, await AddAsync(first.Client, product))).Content.ReadFromJsonAsync<OrderDetail>())!;
        var secondOrder = (await (await CheckoutAsync(second.Client, await AddAsync(second.Client, product))).Content.ReadFromJsonAsync<OrderDetail>())!;
        var denied = await second.Client.GetAsync(Root + "/orders/" + firstOrder.Id);
        var missing = await second.Client.GetAsync(Root + "/orders/" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.Equal(missing.StatusCode, denied.StatusCode);
        Assert.Equal(secondOrder.Id, Assert.Single((await second.Client.GetFromJsonAsync<PageResult<OrderSummary>>(Root + "/orders?customerId=" + first.CustomerId))!.Items).Id);
        using var admin = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        Assert.Equal(2, (await admin.GetFromJsonAsync<PageResult<AdminOrderSummary>>("/api/orders"))!.TotalCount);
        Assert.Equal(firstOrder.Id, (await admin.GetFromJsonAsync<AdminOrderDetail>("/api/orders/" + firstOrder.Id))!.Order.Id);
    }

    [Theory]
    [InlineData(ApplicationRoles.Customer)]
    [InlineData(ApplicationRoles.WarehouseStaff)]
    [InlineData(ApplicationRoles.Viewer)]
    public async Task NonAdmin_CannotReadAdministrativeOrders(string role)
    {
        using var client = await factory.CreateRoleClientAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/orders/" + Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task EveryShoppingMutation_RequiresCsrf()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product);
        customer.Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.PostAsJsonAsync(Root + "/cart/items", new { productId = product, quantity = 1, cartVersion = cart.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.PatchAsJsonAsync(Root + "/cart/items/" + product, new { quantity = 2, cartVersion = cart.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Client.DeleteAsync(Root + $"/cart/items/{product}?version={cart.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CheckoutAsync(customer.Client, cart)).StatusCode);
        Assert.Single((await CartAsync(customer.Client)).Items);
    }

    [Fact]
    public async Task FailureAfterOrderSave_RollsBackOrderAndCartTogether()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product, 2);
        factory.Failure.Enabled = true;
        Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(customer.Client, cart)).StatusCode);
        factory.Failure.Enabled = false;
        Assert.Equal(cart.Version, (await CartAsync(customer.Client)).Version);
        Assert.Equal(2, Assert.Single((await CartAsync(customer.Client)).Items).Quantity);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderItems.ToListAsync());
        Assert.Equal(100, (await db.Inventories.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task ConcurrentCartChanges_WithSameVersion_CannotLoseUpdates()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => customer.Client.PostAsJsonAsync(Root + "/cart/items", new { productId = product, quantity = 1, cartVersion = cart.Version })));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, Assert.Single((await CartAsync(customer.Client)).Items).Quantity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentCheckout_CannotProduceDuplicateOrders(bool sameKey)
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product);
        var key = Guid.NewGuid();
        var responses = await Task.WhenAll(Task.Run(() => CheckoutAsync(customer.Client, cart, key)),
            Task.Run(() => CheckoutAsync(customer.Client, cart, sameKey ? key : Guid.NewGuid())));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, response => Assert.Contains(response.StatusCode,
            new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict }));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Single(await db.Orders.ToListAsync());
        Assert.Empty(await db.CartItems.ToListAsync());
        Assert.Equal(100, (await db.Inventories.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task NegativeStock_IsRejectedEvenWhenOtherWarehouseHasStock()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var warehouse = new Warehouse { Code = "INVALID", Name = "Invalid stock fixture" };
            db.Add(warehouse); await db.SaveChangesAsync();
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints=ON;");
            db.Inventories.Add(new Inventory { ProductId = product, WarehouseId = warehouse.Id, Quantity = -1, Version = Guid.NewGuid() });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints=OFF;");
            await db.Database.CloseConnectionAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(customer.Client, cart)).StatusCode);
        Assert.Single((await CartAsync(customer.Client)).Items);
    }

    [Fact]
    public async Task LogoutThenNewLogin_PreservesDatabaseCart()
    {
        var customer = await CustomerAsync(); var product = await ProductAsync(); var cart = await AddAsync(customer.Client, product, 2);
        var password = "aA!9" + Guid.NewGuid().ToString("N");
        string email;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<DataAccess.Identity.ApplicationUser>>();
            var user = (await users.FindByIdAsync(customer.UserId.ToString()))!;
            Assert.True((await users.RemovePasswordAsync(user)).Succeeded);
            Assert.True((await users.AddPasswordAsync(user, password)).Succeeded);
            email = user.Email!;
        }
        Assert.Equal(HttpStatusCode.NoContent, (await customer.Client.PostAsync("/api/auth/logout", null)).StatusCode);
        using var fresh = factory.CreateClient();
        var csrf = (await fresh.GetFromJsonAsync<WebAPI.Contracts.Auth.CsrfResponse>("/api/auth/csrf"))!;
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password }) };
        login.Headers.Add(csrf.HeaderName, csrf.RequestToken);
        Assert.Equal(HttpStatusCode.NoContent, (await fresh.SendAsync(login)).StatusCode);
        Assert.Equal(cart.Version, (await CartAsync(fresh)).Version);
        Assert.Equal(2, Assert.Single((await CartAsync(fresh)).Items).Quantity);
    }

    [Fact]
    public async Task BusinessLayer_RejectsMissingOwnerAndMissingAdminEvenOutsideController()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CommerceService>();
        var owner = await Assert.ThrowsAsync<CommerceException>(() => service.CartAsync(default));
        Assert.Equal(CommerceFailure.Forbidden, owner.Failure);
        var admin = await Assert.ThrowsAsync<CommerceException>(() => service.AdminOrdersAsync(1, 20, default));
        Assert.Equal(CommerceFailure.Forbidden, admin.Failure);
    }

    public void Dispose() => factory.Dispose();

    private sealed class ShoppingFactory : CustomWebApplicationFactory
    {
        private readonly SqliteConnection anchor = new($"Data Source=shopping-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False;Default Timeout=2");
        public CheckoutFailure Failure { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            anchor.Open();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<InventoryDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
                services.RemoveAll<InventoryDbContext>();
                services.RemoveAll<DbConnection>();
                // Separate connections per request let concurrency tests exercise real database locks.
                services.AddDbContext<InventoryDbContext>((provider, options) => options.UseSqlite(anchor.ConnectionString)
                    .AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>(), Failure));
                using var scope = services.BuildServiceProvider().CreateScope();
                scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.EnsureCreated();
            });
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) anchor.Dispose(); }
    }
    private sealed class CheckoutFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<CartItem>().Any(entry => entry.State == EntityState.Deleted))
                throw new DbUpdateException("Injected failure after order save");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

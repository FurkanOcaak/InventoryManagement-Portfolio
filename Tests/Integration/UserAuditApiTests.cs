using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Business.Common.Security;
using DataAccess.Interceptors;
using DataAccess.Persistence;
using Entities.Common;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Contracts.Auth;

namespace Tests.Integration;

public class UserAuditApiTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();

    private static object Payload(string catalog, string name) => catalog switch
    {
        "products" => new { barcode = "AUDIT-001", name, description = "Audit lifecycle", unitPrice = 12.50m, minimumStock = 2 },
        "warehouses" => new { code = "AUDIT-001", name, location = "Audit location" },
        "customers" => new { code = "AUDIT-001", name, email = (string?)null, phone = (string?)null, taxNumber = (string?)null, address = (string?)null },
        _ => throw new ArgumentOutOfRangeException(nameof(catalog))
    };

    private static async Task<Guid> ActorId(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(user);
        Assert.NotEqual(Guid.Empty, user.Id);
        return user.Id;
    }

    private async Task<BaseEntity> ReadEntity(string catalog, Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return catalog switch
        {
            "products" => await context.Products.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == id),
            "warehouses" => await context.Warehouses.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == id),
            "customers" => await context.Customers.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == id),
            _ => throw new ArgumentOutOfRangeException(nameof(catalog))
        };
    }

    private static async Task AssertSafeContract(HttpResponseMessage response, string catalog)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        string[] expected = catalog switch
        {
            "products" => ["id", "barcode", "barcodeFormat", "name", "description", "unitPrice", "minimumStock", "isActive", "createdAtUtc", "updatedAtUtc"],
            "warehouses" => ["id", "code", "name", "location", "isActive", "createdAtUtc", "updatedAtUtc"],
            "customers" => ["id", "code", "name", "email", "phone", "taxNumber", "address", "isActive", "createdAtUtc", "updatedAtUtc"],
            _ => throw new ArgumentOutOfRangeException(nameof(catalog))
        };
        Assert.Equal(expected.Order(), document.RootElement.EnumerateObject().Select(x => x.Name).Order());
    }

    [Theory]
    [InlineData("products")]
    [InlineData("warehouses")]
    [InlineData("customers")]
    public async Task CatalogLifecycle_ShouldPersistRealActorsAcrossSeparateAuthenticatedRequests(string catalog)
    {
        using var creatorClient = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        using var editorClient = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin, freshSession: true);
        var creator = await ActorId(creatorClient);
        var editor = await ActorId(editorClient);
        Assert.NotEqual(creator, editor);

        using var createdResponse = await creatorClient.PostAsJsonAsync("/api/" + catalog, Payload(catalog, "Initial"));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        await AssertSafeContract(createdResponse, catalog);
        var createdJson = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = createdJson.GetProperty("id").GetGuid();
        var created = await ReadEntity(catalog, id);
        var createdAt = created.CreatedAtUtc;
        Assert.NotEqual(default, createdAt);
        Assert.Equal(creator, created.CreatedByUserId);
        Assert.Null(created.UpdatedByUserId);
        Assert.Null(created.UpdatedAtUtc);
        Assert.Null(created.DeactivatedByUserId);
        Assert.Null(created.DeactivatedAtUtc);

        using var updatedResponse = await editorClient.PutAsJsonAsync($"/api/{catalog}/{id}", Payload(catalog, "Updated"));
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        await AssertSafeContract(updatedResponse, catalog);
        var updated = await ReadEntity(catalog, id);
        Assert.Equal(creator, updated.CreatedByUserId);
        Assert.Equal(createdAt, updated.CreatedAtUtc);
        Assert.Equal(editor, updated.UpdatedByUserId);
        Assert.NotNull(updated.UpdatedAtUtc);
        Assert.True(updated.UpdatedAtUtc >= createdAt);

        using var deletedResponse = await creatorClient.DeleteAsync($"/api/{catalog}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deletedResponse.StatusCode);
        var deleted = await ReadEntity(catalog, id);
        Assert.False(deleted.IsActive);
        Assert.Equal(creator, deleted.DeactivatedByUserId);
        Assert.Equal(creator, deleted.UpdatedByUserId);
        Assert.NotNull(deleted.DeactivatedAtUtc);
        Assert.True(deleted.UpdatedAtUtc >= updated.UpdatedAtUtc);
        Assert.Equal(creator, deleted.CreatedByUserId);
        Assert.Equal(createdAt, deleted.CreatedAtUtc);

        using var reactivatedResponse = await editorClient.PatchAsync($"/api/{catalog}/{id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivatedResponse.StatusCode);
        await AssertSafeContract(reactivatedResponse, catalog);
        var reactivated = await ReadEntity(catalog, id);
        Assert.True(reactivated.IsActive);
        Assert.Null(reactivated.DeactivatedByUserId);
        Assert.Null(reactivated.DeactivatedAtUtc);
        Assert.Equal(editor, reactivated.UpdatedByUserId);
        Assert.True(reactivated.UpdatedAtUtc >= deleted.UpdatedAtUtc);
        Assert.Equal(creator, reactivated.CreatedByUserId);
        Assert.Equal(createdAt, reactivated.CreatedAtUtc);
    }

    [Theory]
    [InlineData("products")]
    [InlineData("warehouses")]
    [InlineData("customers")]
    public async Task LegacyCatalogUpdateAndReactivation_ShouldNeverBackfillCreationIdentity(string catalog)
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var actor = await ActorId(client);
        Guid id;
        DateTime createdAt;
        using (var scope = _factory.Services.CreateScope())
        {
            Assert.Null(scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId);
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            BaseEntity entity = catalog switch
            {
                "products" => new Product { Barcode = "AUDIT-001", Name = "Legacy", UnitPrice = 1 },
                "warehouses" => new Warehouse { Code = "AUDIT-001", Name = "Legacy" },
                "customers" => new Customer { Code = "AUDIT-001", Name = "Legacy" },
                _ => throw new ArgumentOutOfRangeException(nameof(catalog))
            };
            context.Add(entity);
            await context.SaveChangesAsync();
            id = entity.Id;
            createdAt = entity.CreatedAtUtc;
            Assert.Null(entity.CreatedByUserId);
        }
        using var update = await client.PutAsJsonAsync($"/api/{catalog}/{id}", Payload(catalog, "Updated legacy"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadEntity(catalog, id);
        Assert.Null(updated.CreatedByUserId);
        Assert.Equal(actor, updated.UpdatedByUserId);
        using var delete = await client.DeleteAsync($"/api/{catalog}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(actor, (await ReadEntity(catalog, id)).DeactivatedByUserId);
        using var reactivate = await client.PatchAsync($"/api/{catalog}/{id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        var restored = await ReadEntity(catalog, id);
        Assert.Null(restored.CreatedByUserId);
        Assert.Equal(createdAt, restored.CreatedAtUtc);
        Assert.Equal(actor, restored.UpdatedByUserId);
        Assert.Null(restored.DeactivatedByUserId);
        Assert.Null(restored.DeactivatedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaffStockInAndOut_ShouldAttributeMovementsAndPreserveExistingHistory(bool legacyInventory)
    {
        using var firstClient = await _factory.CreateRoleClientAsync(ApplicationRoles.WarehouseStaff);
        using var secondClient = await _factory.CreateRoleClientAsync(ApplicationRoles.WarehouseStaff, freshSession: true);
        var firstActor = await ActorId(firstClient);
        var secondActor = await ActorId(secondClient);
        Assert.NotEqual(firstActor, secondActor);
        var productId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var legacyMovementId = Guid.NewGuid();
        DateTime? legacyCreatedAt = null;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            context.Products.Add(new Product { Id = productId, Barcode = "AUDIT-STOCK", Name = "Audit product", UnitPrice = 1 });
            context.Warehouses.Add(new Warehouse { Id = warehouseId, Code = "AUDIT-STOCK", Name = "Audit warehouse" });
            if (legacyInventory)
            {
                var inventory = new Inventory { Id = Guid.NewGuid(), ProductId = productId, WarehouseId = warehouseId, Quantity = 10, Version = Guid.NewGuid() };
                context.Inventories.Add(inventory);
                var movement = new StockMovement { Id = legacyMovementId, InventoryId = inventory.Id, MovementType = MovementType.StockIn, Quantity = 10, QuantityBefore = 0, QuantityAfter = 10 };
                context.StockMovements.Add(movement);
                await context.SaveChangesAsync();
                legacyCreatedAt = movement.CreatedAtUtc;
            }
            else await context.SaveChangesAsync();
        }
        var before = legacyInventory ? 10 : 0;
        using var stockIn = await firstClient.PostAsJsonAsync("/api/stock/in", new { productId, warehouseId, quantity = 10 });
        Assert.Equal(HttpStatusCode.Created, stockIn.StatusCode);
        var firstJson = await stockIn.Content.ReadFromJsonAsync<JsonElement>();
        var firstId = firstJson.GetProperty("stockMovementId").GetGuid();
        var firstTime = firstJson.GetProperty("createdAtUtc").GetDateTime();
        using var stockOut = await secondClient.PostAsJsonAsync("/api/stock/out", new { productId, warehouseId, quantity = 3 });
        Assert.Equal(HttpStatusCode.Created, stockOut.StatusCode);
        var secondJson = await stockOut.Content.ReadFromJsonAsync<JsonElement>();
        var secondId = secondJson.GetProperty("stockMovementId").GetGuid();

        using var verificationScope = _factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var savedInventory = await db.Inventories.AsNoTracking().SingleAsync();
        Assert.Equal(before + 7, savedInventory.Quantity);
        Assert.Equal(legacyInventory ? (Guid?)null : firstActor, savedInventory.CreatedByUserId);
        Assert.Equal(secondActor, savedInventory.UpdatedByUserId);
        var incoming = await db.StockMovements.AsNoTracking().SingleAsync(x => x.Id == firstId);
        var outgoing = await db.StockMovements.AsNoTracking().SingleAsync(x => x.Id == secondId);
        Assert.Equal(firstActor, incoming.CreatedByUserId);
        Assert.Equal(secondActor, outgoing.CreatedByUserId);
        Assert.Null(incoming.UpdatedByUserId);
        Assert.Null(outgoing.UpdatedByUserId);
        Assert.Equal(firstTime, incoming.CreatedAtUtc);
        Assert.Equal(secondJson.GetProperty("createdAtUtc").GetDateTime(), outgoing.CreatedAtUtc);
        Assert.Equal(MovementType.StockIn, incoming.MovementType);
        Assert.Equal(MovementType.StockOut, outgoing.MovementType);
        Assert.Equal(before, incoming.QuantityBefore);
        Assert.Equal(before + 10, incoming.QuantityAfter);
        Assert.Equal(before + 10, outgoing.QuantityBefore);
        Assert.Equal(before + 7, outgoing.QuantityAfter);
        Assert.Equal(legacyInventory ? 3 : 2, await db.StockMovements.CountAsync());
        if (legacyInventory)
        {
            var legacy = await db.StockMovements.AsNoTracking().SingleAsync(x => x.Id == legacyMovementId);
            Assert.Null(legacy.CreatedByUserId);
            Assert.Null(legacy.UpdatedByUserId);
            Assert.Equal(legacyCreatedAt, legacy.CreatedAtUtc);
        }
    }

    [Fact]
    public void RequestServices_ShouldUseSeparateAuditScopesAndHaveNoUserOutsideHttpRequests()
    {
        using var first = _factory.Services.CreateScope();
        using var second = _factory.Services.CreateScope();
        var interceptor = first.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>();
        Assert.Same(interceptor, first.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
        Assert.NotSame(interceptor, second.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
        Assert.Null(first.ServiceProvider.GetRequiredService<ICurrentUser>().UserId);
        Assert.Null(second.ServiceProvider.GetRequiredService<ICurrentUser>().UserId);
    }

    public void Dispose() => _factory.Dispose();
}

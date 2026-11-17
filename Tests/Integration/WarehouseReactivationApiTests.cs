using Business.Common.Security;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Business.Features.Products.DTOs;
using Business.Features.Stock.DTOs;
using Business.Features.Warehouses.DTOs;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class WarehouseReactivationApiTests
{
    private static async Task<WarehouseResponse> CreateWarehouseAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/warehouses", new WarehouseCreateRequest
        {
            Code = code,
            Name = "Reactivation test warehouse",
            Location = "Preserve warehouse information",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var warehouse = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(warehouse);
        return warehouse;
    }

    [Fact]
    public async Task GetInactive_WhenNoWarehousesExist_ShouldReturnEmptyList()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.GetAsync("/api/warehouses/inactive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var warehouses = await response.Content.ReadFromJsonAsync<List<WarehouseResponse>>();
        Assert.NotNull(warehouses);
        Assert.Empty(warehouses);
    }

    [Fact]
    public async Task GetInactive_ShouldReturnOnlyDeactivatedWarehouses()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var active = await CreateWarehouseAsync(client, "REACT-ACTIVE");
        var inactive = await CreateWarehouseAsync(client, "REACT-INACTIVE");

        var deleteResponse = await client.DeleteAsync($"/api/warehouses/{inactive.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var response = await client.GetAsync("/api/warehouses/inactive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var warehouses = await response.Content.ReadFromJsonAsync<List<WarehouseResponse>>();
        Assert.NotNull(warehouses);
        var warehouse = Assert.Single(warehouses);
        Assert.Equal(inactive.Id, warehouse.Id);
        Assert.False(warehouse.IsActive);
        Assert.DoesNotContain(warehouses, item => item.Id == active.Id);

        var activeWarehouses = await client.GetFromJsonAsync<List<WarehouseResponse>>("/api/warehouses");
        Assert.NotNull(activeWarehouses);
        Assert.Equal(active.Id, Assert.Single(activeWarehouses).Id);
        Assert.All(activeWarehouses, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task Reactivate_ShouldRestoreActiveVisibilityAndClearDeactivationAudit()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var created = await CreateWarehouseAsync(client, "REACT-LIFECYCLE");
        var deleteResponse = await client.DeleteAsync($"/api/warehouses/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/warehouses/{created.Id}")).StatusCode);
        var inactiveWarehouses = await client.GetFromJsonAsync<List<WarehouseResponse>>("/api/warehouses/inactive");
        Assert.NotNull(inactiveWarehouses);
        Assert.Equal(created.Id, Assert.Single(inactiveWarehouses).Id);

        DateTime? lastUpdatedAtUtc;
        var deactivatedBy = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            var warehouse = await context.Warehouses.IgnoreQueryFilters().SingleAsync(item => item.Id == created.Id);
            Assert.NotNull(warehouse.DeactivatedAtUtc);
            warehouse.DeactivatedByUserId = deactivatedBy;
            await context.SaveChangesAsync();
            lastUpdatedAtUtc = warehouse.UpdatedAtUtc;
        }

        var response = await client.PatchAsync($"/api/warehouses/{created.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reactivated = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(reactivated);
        Assert.True(reactivated.IsActive);
        Assert.Equal(created.Id, reactivated.Id);
        Assert.Equal(created.Code, reactivated.Code);
        Assert.Equal(created.Name, reactivated.Name);
        Assert.Equal(created.Location, reactivated.Location);
        Assert.Equal(created.CreatedAtUtc, reactivated.CreatedAtUtc);
        Assert.NotNull(reactivated.UpdatedAtUtc);
        Assert.True(reactivated.UpdatedAtUtc > lastUpdatedAtUtc);

        var activeWarehouses = await client.GetFromJsonAsync<List<WarehouseResponse>>("/api/warehouses");
        Assert.NotNull(activeWarehouses);
        Assert.Equal(created.Id, Assert.Single(activeWarehouses).Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/warehouses/{created.Id}")).StatusCode);
        var remainingInactive = await client.GetFromJsonAsync<List<WarehouseResponse>>("/api/warehouses/inactive");
        Assert.NotNull(remainingInactive);
        Assert.Empty(remainingInactive);

        using var verificationScope = factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var persisted = await dbContext.Warehouses.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Equal(created.Id, persisted.Id);
        Assert.True(persisted.IsActive);
        Assert.Null(persisted.DeactivatedAtUtc);
        Assert.Null(persisted.DeactivatedByUserId);
        Assert.Equal(reactivated.UpdatedAtUtc, persisted.UpdatedAtUtc);
        Assert.Equal(created.CreatedAtUtc, persisted.CreatedAtUtc);
        Assert.Equal((await client.GetFromJsonAsync<WebAPI.Contracts.Auth.CurrentUserResponse>("/api/auth/me"))!.Id,
            persisted.CreatedByUserId);
        Assert.Empty(await dbContext.Inventories.ToListAsync());
        Assert.Empty(await dbContext.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task Reactivate_WhenAlreadyActive_ShouldReturnConflictWithoutChangingWarehouse()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var created = await CreateWarehouseAsync(client, "REACT-ALREADY-ACTIVE");

        var response = await client.PatchAsync($"/api/warehouses/{created.Id}/reactivate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Depo zaten aktif", problem.Title);
        var persisted = await client.GetFromJsonAsync<WarehouseResponse>($"/api/warehouses/{created.Id}");
        Assert.NotNull(persisted);
        Assert.True(persisted.IsActive);
        Assert.Equal(created.UpdatedAtUtc, persisted.UpdatedAtUtc);
    }

    [Fact]
    public async Task Reactivate_WhenMissing_ShouldReturnNotFound()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.PatchAsync($"/api/warehouses/{Guid.NewGuid()}/reactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task InactiveCode_ShouldRemainReservedBeforeAndAfterReactivation()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var original = await CreateWarehouseAsync(client, "REACT-RESERVED");
        var other = await CreateWarehouseAsync(client, "REACT-OTHER");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/warehouses/{original.Id}")).StatusCode);

        var duplicateRequest = new WarehouseCreateRequest { Code = original.Code, Name = "Duplicate" };
        var duplicateCreate = await client.PostAsJsonAsync("/api/warehouses", duplicateRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateCreate.StatusCode);
        var duplicateUpdate = await client.PutAsJsonAsync($"/api/warehouses/{other.Id}", new WarehouseUpdateRequest
        {
            Code = original.Code, Name = other.Name, Location = other.Location
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateUpdate.StatusCode);

        var reactivate = await client.PatchAsync($"/api/warehouses/{original.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        var duplicateAfterReactivation = await client.PostAsJsonAsync("/api/warehouses", duplicateRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateAfterReactivation.StatusCode);
        var warehouses = await client.GetFromJsonAsync<List<WarehouseResponse>>("/api/warehouses");
        Assert.NotNull(warehouses);
        Assert.Equal(2, warehouses.Count);
        Assert.Single(warehouses, item => item.Code == original.Code);
    }

    [Fact]
    public async Task Reactivate_ShouldPreserveExistingInventoryAndStockMovements()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var warehouse = await CreateWarehouseAsync(client, "REACT-STOCK-WAREHOUSE");
        var productResponse = await client.PostAsJsonAsync("/api/products", new ProductCreateRequest
        {
            Barcode = "REACT-WH-STOCK-PRODUCT",
            Name = "Stock relationship test",
            UnitPrice = 10m,
            MinimumStock = 0
        });
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        var stockResponse = await client.PostAsJsonAsync("/api/stock/in", new StockOperationRequest
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            Quantity = 12,
            Note = "Preserve existing movement"
        });
        Assert.Equal(HttpStatusCode.Created, stockResponse.StatusCode);

        string inventoryBefore;
        string movementsBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            var inventory = await context.Inventories.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.Equal(warehouse.Id, inventory.WarehouseId);
            Assert.Equal(product.Id, inventory.ProductId);
            Assert.Equal(12, inventory.Quantity);
            var movements = await context.StockMovements.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            Assert.Equal(inventory.Id, Assert.Single(movements).InventoryId);
            inventoryBefore = JsonSerializer.Serialize(inventory);
            movementsBefore = JsonSerializer.Serialize(movements);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/warehouses/{warehouse.Id}")).StatusCode);
        var response = await client.PatchAsync($"/api/warehouses/{warehouse.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reactivated = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(reactivated);
        Assert.Equal(warehouse.Code, reactivated.Code);
        Assert.Equal(warehouse.Name, reactivated.Name);
        Assert.Equal(warehouse.Location, reactivated.Location);
        Assert.Equal(warehouse.CreatedAtUtc, reactivated.CreatedAtUtc);

        using var verificationScope = factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var inventoryAfter = await dbContext.Inventories.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        var movementsAfter = await dbContext.StockMovements.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(inventoryBefore, JsonSerializer.Serialize(inventoryAfter));
        Assert.Equal(movementsBefore, JsonSerializer.Serialize(movementsAfter));
    }
}

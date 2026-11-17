using Business.Common.Security;
using System.Net;
using System.Net.Http.Json;
using Business.Features.Products.DTOs;
using Business.Features.Stock.DTOs;
using Business.Features.Warehouses.DTOs;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class StockApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public StockApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];

    private static ProductCreateRequest CreateProductRequest(string barcode, string name)
    {
        return new ProductCreateRequest
        {
            Barcode = barcode,
            Name = name,
            Description = "Stock test product",
            UnitPrice = 49.99m,
            MinimumStock = 5
        };
    }

    private static WarehouseCreateRequest CreateWarehouseRequest(string code, string name)
    {
        return new WarehouseCreateRequest
        {
            Code = code,
            Name = name,
            Location = "Istanbul"
        };
    }

    private static StockOperationRequest CreateStockRequest(Guid productId, Guid warehouseId, int quantity, string? note = null)
    {
        return new StockOperationRequest
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Quantity = quantity,
            Note = note
        };
    }

    private async Task<ProductResponse> CreateProductAsync(HttpClient client, string barcode, string name)
    {
        var response = await client.PostAsJsonAsync("/api/products", CreateProductRequest(barcode, name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var product = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        return product!;
    }

    private async Task<WarehouseResponse> CreateWarehouseAsync(HttpClient client, string code, string name)
    {
        var response = await client.PostAsJsonAsync("/api/warehouses", CreateWarehouseRequest(code, name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var warehouse = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(warehouse);
        return warehouse!;
    }

    [Fact]
    public async Task PostStockIn_ShouldCreateInventoryAndMovement()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var barcode = $"STK-IN-{NewSuffix()}";
        var product = await CreateProductAsync(client, barcode, "Stock In Product");
        var warehouse = await CreateWarehouseAsync(client, $"WH-IN-{NewSuffix()}", "Stock In Warehouse");

        var request = CreateStockRequest(product.Id, warehouse.Id, 10, "Initial stock");
        var response = await client.PostAsJsonAsync("/api/stock/in", request);
        var result = await response.Content.ReadFromJsonAsync<StockOperationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(product.Id, result!.ProductId);
        Assert.Equal(warehouse.Id, result.WarehouseId);
        Assert.Equal(MovementType.StockIn, result.MovementType);
        Assert.Equal(10, result.Quantity);
        Assert.Equal(0, result.QuantityBefore);
        Assert.Equal(10, result.QuantityAfter);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var inventory = await dbContext.Inventories
            .SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse.Id);

        Assert.Equal(10, inventory.Quantity);
        Assert.NotEqual(Guid.Empty, inventory.Version);

        var movement = await dbContext.StockMovements
            .SingleAsync(x => x.InventoryId == inventory.Id);

        Assert.Equal(MovementType.StockIn, movement.MovementType);
        Assert.Equal(10, movement.Quantity);
        Assert.Equal(0, movement.QuantityBefore);
        Assert.Equal(10, movement.QuantityAfter);
    }

    [Fact]
    public async Task PostStockIn_WhenInventoryExists_ShouldIncreaseCurrentBalance()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, $"STK-EX-{NewSuffix()}", "Existing Inventory Product");
        var warehouse = await CreateWarehouseAsync(client, $"WH-EX-{NewSuffix()}", "Existing Inventory Warehouse");

        var first = CreateStockRequest(product.Id, warehouse.Id, 15, "First entry");
        var firstResponse = await client.PostAsJsonAsync("/api/stock/in", first);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var second = CreateStockRequest(product.Id, warehouse.Id, 8, "Second entry");
        var secondResponse = await client.PostAsJsonAsync("/api/stock/in", second);
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<StockOperationResponse>();

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotNull(secondResult);
        Assert.Equal(8, secondResult!.Quantity);
        Assert.Equal(15, secondResult.QuantityBefore);
        Assert.Equal(23, secondResult.QuantityAfter);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var inventory = await dbContext.Inventories
            .SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse.Id);

        Assert.Equal(23, inventory.Quantity);
        Assert.NotEqual(Guid.Empty, inventory.Version);
    }

    [Fact]
    public async Task PostStockOut_ShouldDecreaseBalanceAndPersistMovement()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, $"STK-OUT-{NewSuffix()}", "Stock Out Product");
        var warehouse = await CreateWarehouseAsync(client, $"WH-OUT-{NewSuffix()}", "Stock Out Warehouse");

        var initialResponse = await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, warehouse.Id, 30, "Initial"));
        Assert.Equal(HttpStatusCode.Created, initialResponse.StatusCode);

        var response = await client.PostAsJsonAsync("/api/stock/out", CreateStockRequest(product.Id, warehouse.Id, 12, "Shipment"));
        var result = await response.Content.ReadFromJsonAsync<StockOperationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(12, result!.Quantity);
        Assert.Equal(30, result.QuantityBefore);
        Assert.Equal(18, result.QuantityAfter);
        Assert.Equal(MovementType.StockOut, result.MovementType);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var inventory = await dbContext.Inventories
            .SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse.Id);

        Assert.Equal(18, inventory.Quantity);

        var movement = await dbContext.StockMovements
            .SingleAsync(x => x.InventoryId == inventory.Id && x.MovementType == MovementType.StockOut);

        Assert.Equal(12, movement.Quantity);
        Assert.Equal(30, movement.QuantityBefore);
        Assert.Equal(18, movement.QuantityAfter);
    }

    [Fact]
    public async Task PostStockOut_WhenInsufficient_ShouldReturnConflictAndLeaveDataUntouched()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, $"STK-LOW-{NewSuffix()}", "Low Stock Product");
        var warehouse = await CreateWarehouseAsync(client, $"WH-LOW-{NewSuffix()}", "Low Stock Warehouse");

        var initial = CreateStockRequest(product.Id, warehouse.Id, 10, "Initial stock");
        var initialResponse = await client.PostAsJsonAsync("/api/stock/in", initial);
        Assert.Equal(HttpStatusCode.Created, initialResponse.StatusCode);

        var failingRequest = CreateStockRequest(product.Id, warehouse.Id, 15, "Too much");
        var failingResponse = await client.PostAsJsonAsync("/api/stock/out", failingRequest);

        Assert.Equal(HttpStatusCode.Conflict, failingResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var inventory = await dbContext.Inventories
            .SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse.Id);

        Assert.Equal(10, inventory.Quantity);
        Assert.Equal(1, await dbContext.StockMovements.CountAsync(x => x.InventoryId == inventory.Id));
    }

    [Fact]
    public async Task StockOperation_WithZeroOrNegativeQuantity_ShouldReturnBadRequest()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, $"STK-VAL-{NewSuffix()}", "Validation Product");
        var warehouse = await CreateWarehouseAsync(client, $"WH-VAL-{NewSuffix()}", "Validation Warehouse");

        var zeroResponse = await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, warehouse.Id, 0));
        Assert.Equal(HttpStatusCode.BadRequest, zeroResponse.StatusCode);

        var negativeResponse = await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, warehouse.Id, -1));
        Assert.Equal(HttpStatusCode.BadRequest, negativeResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        Assert.Empty(await dbContext.Inventories.Where(x => x.ProductId == product.Id && x.WarehouseId == warehouse.Id).ToListAsync());
    }

    [Fact]
    public async Task StockOperation_WithUnknownProductOrWarehouse_ShouldReturnNotFound()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var warehouse = await CreateWarehouseAsync(client, $"WH-404-{NewSuffix()}", "Warehouse 404");
        var unknownProductId = Guid.NewGuid();

        var productMissingResponse = await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(unknownProductId, warehouse.Id, 5));
        Assert.Equal(HttpStatusCode.NotFound, productMissingResponse.StatusCode);

        var product = await CreateProductAsync(client, $"STK-404-{NewSuffix()}", "Product 404");
        var unknownWarehouseId = Guid.NewGuid();

        var warehouseMissingResponse = await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, unknownWarehouseId, 5));
        Assert.Equal(HttpStatusCode.NotFound, warehouseMissingResponse.StatusCode);
    }

    [Fact]
    public async Task StockOperation_WithInactiveProductOrWarehouse_ShouldNotBeAllowed()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, $"STK-INACT-{NewSuffix()}", "Inactive Product");
        var warehouse = await CreateWarehouseAsync(client, $"WH-INACT-{NewSuffix()}", "Inactive Warehouse");

        var deactivateProductResponse = await client.DeleteAsync($"/api/products/{product.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivateProductResponse.StatusCode);

        var deactivateWarehouseResponse = await client.DeleteAsync($"/api/warehouses/{warehouse.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivateWarehouseResponse.StatusCode);

        var productRequest = CreateStockRequest(product.Id, Guid.NewGuid(), 5);
        var productResponse = await client.PostAsJsonAsync("/api/stock/in", productRequest);
        Assert.Equal(HttpStatusCode.NotFound, productResponse.StatusCode);

        var warehouseRequest = CreateStockRequest(Guid.NewGuid(), warehouse.Id, 5);
        var warehouseResponse = await client.PostAsJsonAsync("/api/stock/in", warehouseRequest);
        Assert.Equal(HttpStatusCode.NotFound, warehouseResponse.StatusCode);
    }

    [Fact]
    public async Task StockOperation_ShouldMaintainIndependentBalancesPerWarehouse()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, $"STK-DB-{NewSuffix()}", "Warehouse Independent Product");
        var warehouse1 = await CreateWarehouseAsync(client, $"WH-1-{NewSuffix()}", "Warehouse 1");
        var warehouse2 = await CreateWarehouseAsync(client, $"WH-2-{NewSuffix()}", "Warehouse 2");

        await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, warehouse1.Id, 10));
        await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, warehouse2.Id, 7));

        var response1 = await client.PostAsJsonAsync("/api/stock/out", CreateStockRequest(product.Id, warehouse1.Id, 3));
        var response2 = await client.PostAsJsonAsync("/api/stock/out", CreateStockRequest(product.Id, warehouse2.Id, 2));

        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var inventory1 = await dbContext.Inventories.SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse1.Id);
        var inventory2 = await dbContext.Inventories.SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse2.Id);

        Assert.Equal(7, inventory1.Quantity);
        Assert.Equal(5, inventory2.Quantity);
    }

    [Fact]
    public async Task StockOperation_ShouldUpdateVersionOnSuccessfulBalanceChange()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, $"STK-VER-{NewSuffix()}", "Version Product");
        var warehouse = await CreateWarehouseAsync(client, $"WH-VER-{NewSuffix()}", "Version Warehouse");

        var firstResponse = await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, warehouse.Id, 5));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            var inventory = await dbContext.Inventories
                .SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse.Id);

            var originalVersion = inventory.Version;

            var secondResponse = await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, warehouse.Id, 3));
            Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

            using var verificationScope = _factory.Services.CreateScope();
            var verificationContext = verificationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var updatedInventory = await verificationContext.Inventories
                .SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse.Id);

            Assert.NotEqual(originalVersion, updatedInventory.Version);
            Assert.Equal(8, updatedInventory.Quantity);
        }
    }

    [Fact]
    public async Task StockOperation_ShouldAssociateMovementWithInventoryId()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, $"STK-REL-{NewSuffix()}", "Relationship Product");
        var warehouse = await CreateWarehouseAsync(client, $"WH-REL-{NewSuffix()}", "Relationship Warehouse");

        var response = await client.PostAsJsonAsync("/api/stock/in", CreateStockRequest(product.Id, warehouse.Id, 11, "Relationship test"));
        var result = await response.Content.ReadFromJsonAsync<StockOperationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var inventory = await dbContext.Inventories
            .SingleAsync(x => x.ProductId == product.Id && x.WarehouseId == warehouse.Id);

        var movement = await dbContext.StockMovements
            .SingleAsync(x => x.InventoryId == inventory.Id && x.MovementType == MovementType.StockIn);

        Assert.Equal(inventory.Id, movement.InventoryId);
        Assert.Equal(11, movement.Quantity);
    }

    [Fact]
    public async Task Concurrency_ShouldThrowWhenTwoContextsUpdateSameInventoryVersion()
    {
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        var dbContext1 = scope1.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var dbContext2 = scope2.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var product = new Product
        {
            Barcode = $"STK-CON-{NewSuffix()}",
            Name = "Concurrency Product",
            Description = "Test",
            UnitPrice = 10m,
            MinimumStock = 1,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var warehouse = new Warehouse
        {
            Code = $"WH-CON-{NewSuffix()}",
            Name = "Concurrency Warehouse",
            Location = "Istanbul",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext1.Products.Add(product);
        dbContext1.Warehouses.Add(warehouse);
        await dbContext1.SaveChangesAsync();

        var inventory = new Inventory
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            Quantity = 10,
            Version = Guid.NewGuid(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext1.Inventories.Add(inventory);
        await dbContext1.SaveChangesAsync();

        var firstSnapshot = await dbContext1.Inventories
            .SingleAsync(x => x.Id == inventory.Id);

        var secondSnapshot = await dbContext2.Inventories
            .SingleAsync(x => x.Id == inventory.Id);

        firstSnapshot.Quantity = 15;
        firstSnapshot.Version = Guid.NewGuid();
        await dbContext1.SaveChangesAsync();

        secondSnapshot.Quantity = 30;
        secondSnapshot.Version = firstSnapshot.Version;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await dbContext2.SaveChangesAsync());
    }
}

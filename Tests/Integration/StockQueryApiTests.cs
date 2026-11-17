using Business.Common.Security;
﻿
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

public class StockQueryApiTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public StockQueryApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string NewCode() =>
        Guid.NewGuid().ToString("N")[..8];

    private static async Task<ProductResponse> CreateProductAsync(
        HttpClient client, int minimumStock = 5)
    {
        var response = await client.PostAsJsonAsync(
            "/api/products",
            new ProductCreateRequest
            {
                Barcode = $"QUERY-{NewCode()}",
                Name = "Query Test Product",
                UnitPrice = 100m,
                MinimumStock = minimumStock
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content
            .ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<WarehouseResponse> CreateWarehouseAsync(
        HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/warehouses",
            new WarehouseCreateRequest
            {
                Code = $"QWH-{NewCode()}",
                Name = "Query Test Warehouse",
                Location = "Ankara"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content
            .ReadFromJsonAsync<WarehouseResponse>())!;
    }

    private static async Task StockAsync(
        HttpClient client,
        string operation,
        Guid productId,
        Guid warehouseId,
        int quantity)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/stock/{operation}",
            new StockOperationRequest
            {
                ProductId = productId,
                WarehouseId = warehouseId,
                Quantity = quantity
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetStock_ShouldReturnIndependentWarehouseBalances()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client);
        var first = await CreateWarehouseAsync(client);
        var second = await CreateWarehouseAsync(client);

        await StockAsync(client, "in", product.Id, first.Id, 20);
        await StockAsync(client, "in", product.Id, second.Id, 8);

        var items = await client.GetFromJsonAsync<
            List<InventoryStockItemResponse>>(
            $"/api/stock?productId={product.Id}");

        Assert.NotNull(items);
        Assert.Equal(2, items.Count);

        Assert.Contains(items,
            x => x.WarehouseId == first.Id && x.Quantity == 20);

        Assert.Contains(items,
            x => x.WarehouseId == second.Id && x.Quantity == 8);

        var filtered = await client.GetFromJsonAsync<
            List<InventoryStockItemResponse>>(
            $"/api/stock?productId={product.Id}&warehouseId={first.Id}");

        Assert.NotNull(filtered);
        Assert.Single(filtered);
        Assert.Equal(20, filtered[0].Quantity);
    }

    [Fact]
    public async Task GetMovements_ShouldReturnStockInAndStockOutHistory()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client);
        var warehouse = await CreateWarehouseAsync(client);

        await StockAsync(client, "in", product.Id, warehouse.Id, 15);
        await StockAsync(client, "out", product.Id, warehouse.Id, 6);

        var movements = await client.GetFromJsonAsync<
            List<StockMovementHistoryResponse>>(
            $"/api/stock/movements?productId={product.Id}");

        Assert.NotNull(movements);
        Assert.Equal(2, movements.Count);

        Assert.Contains(movements, x =>
            x.MovementType == MovementType.StockIn &&
            x.QuantityBefore == 0 &&
            x.QuantityAfter == 15);

        Assert.Contains(movements, x =>
            x.MovementType == MovementType.StockOut &&
            x.QuantityBefore == 15 &&
            x.QuantityAfter == 9);
    }

    [Fact]
    public async Task GetMovements_ShouldRetainInactiveProductAndWarehouseHistory()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client);
        var warehouse = await CreateWarehouseAsync(client);

        await StockAsync(client, "in", product.Id, warehouse.Id, 10);

        var deleteProduct =
            await client.DeleteAsync($"/api/products/{product.Id}");

        var deleteWarehouse =
            await client.DeleteAsync($"/api/warehouses/{warehouse.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteProduct.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleteWarehouse.StatusCode);

        var stock = await client.GetFromJsonAsync<
            List<InventoryStockItemResponse>>(
            $"/api/stock?productId={product.Id}");

        Assert.NotNull(stock);
        Assert.Empty(stock);

        var movements = await client.GetFromJsonAsync<
            List<StockMovementHistoryResponse>>(
            $"/api/stock/movements?productId={product.Id}");

        Assert.NotNull(movements);
        Assert.Single(movements);
        Assert.Equal(10, movements[0].QuantityAfter);
    }

    [Fact]
    public async Task GetLowStock_ShouldOnlyReturnCriticalBalances()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client, minimumStock: 5);
        var lowWarehouse = await CreateWarehouseAsync(client);
        var normalWarehouse = await CreateWarehouseAsync(client);

        await StockAsync(client, "in", product.Id, lowWarehouse.Id, 3);
        await StockAsync(client, "in", product.Id, normalWarehouse.Id, 9);

        var allLowStock = await client.GetFromJsonAsync<
            List<LowStockItemResponse>>("/api/stock/low-stock");

        Assert.NotNull(allLowStock);

        var productLowStock = allLowStock
            .Where(x => x.ProductId == product.Id)
            .ToList();

        Assert.Single(productLowStock);
        Assert.Equal(lowWarehouse.Id, productLowStock[0].WarehouseId);
        Assert.Equal(3, productLowStock[0].Quantity);
        Assert.Equal(5, productLowStock[0].MinimumStock);
        Assert.Equal(2, productLowStock[0].Shortage);
    }

    [Fact]
    public async Task GetMovements_ShouldRespectLimit()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client);
        var warehouse = await CreateWarehouseAsync(client);

        await StockAsync(client, "in", product.Id, warehouse.Id, 10);
        await StockAsync(client, "in", product.Id, warehouse.Id, 4);
        await StockAsync(client, "out", product.Id, warehouse.Id, 2);

        var movements = await client.GetFromJsonAsync<
            List<StockMovementHistoryResponse>>(
            $"/api/stock/movements?productId={product.Id}&limit=2");

        Assert.NotNull(movements);
        Assert.Equal(2, movements.Count);

        var invalid = await client.GetAsync(
            "/api/stock/movements?limit=101");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task GetEndpoints_ShouldNotModifyInventoryBalanceOrVersion()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var product = await CreateProductAsync(client);
        var warehouse = await CreateWarehouseAsync(client);

        await StockAsync(client, "in", product.Id, warehouse.Id, 4);

        Guid originalVersion;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<InventoryDbContext>();

            originalVersion = await context.Inventories
                .Where(x => x.ProductId == product.Id &&
                            x.WarehouseId == warehouse.Id)
                .Select(x => x.Version)
                .SingleAsync();
        }

        var stockResponse = await client.GetAsync(
            $"/api/stock?productId={product.Id}");

        var movementsResponse = await client.GetAsync(
            $"/api/stock/movements?productId={product.Id}");

        var lowStockResponse = await client.GetAsync(
            "/api/stock/low-stock");

        Assert.Equal(HttpStatusCode.OK, stockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, movementsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lowStockResponse.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<InventoryDbContext>();

        var inventory = await verificationContext.Inventories
            .AsNoTracking()
            .SingleAsync(x =>
                x.ProductId == product.Id &&
                x.WarehouseId == warehouse.Id);

        Assert.Equal(4, inventory.Quantity);
        Assert.Equal(originalVersion, inventory.Version);
    }
}

using Business.Common.Security;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Business.Features.Products.DTOs;
using DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class ProductApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static ProductCreateRequest CreateProductRequest(string barcode, string name, decimal unitPrice = 49.99m, int minimumStock = 5)
    {
        return new ProductCreateRequest
        {
            Barcode = barcode,
            Name = name,
            Description = "Integration test product",
            UnitPrice = unitPrice,
            MinimumStock = minimumStock
        };
    }

    private static ProductUpdateRequest CreateProductUpdateRequest(string barcode, string name, decimal unitPrice = 59.99m, int minimumStock = 8)
    {
        return new ProductUpdateRequest
        {
            Barcode = barcode,
            Name = name,
            Description = "Updated product description",
            UnitPrice = unitPrice,
            MinimumStock = minimumStock
        };
    }

    [Fact]
    public async Task PostProduct_ShouldCreateProductAndReturnCreatedStatus()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var request = CreateProductRequest("SKU-POST-001", "Laptop");

        var postResponse = await client.PostAsJsonAsync("/api/products", request);
        var createdProduct = await postResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        Assert.NotNull(createdProduct);
        Assert.NotEqual(Guid.Empty, createdProduct!.Id);
        Assert.Equal(request.Barcode, createdProduct.Barcode);
        Assert.Equal(request.Name, createdProduct.Name);
        Assert.True(createdProduct.IsActive);
        Assert.NotEqual(default, createdProduct.CreatedAtUtc);
        Assert.Null(createdProduct.UpdatedAtUtc);
        Assert.NotNull(postResponse.Headers.Location);
    }

    [Fact]
    public async Task GetProducts_ShouldReturnOnlyActiveProducts()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var activeRequest = CreateProductRequest("SKU-GET-001", "Active Product");
        var activeResponse = await client.PostAsJsonAsync("/api/products", activeRequest);
        Assert.Equal(HttpStatusCode.Created, activeResponse.StatusCode);

        var inactiveRequest = CreateProductRequest("SKU-GET-002", "Inactive Product");
        var inactiveResponse = await client.PostAsJsonAsync("/api/products", inactiveRequest);
        Assert.Equal(HttpStatusCode.Created, inactiveResponse.StatusCode);
        var createdInactive = await inactiveResponse.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(createdInactive);

        var deleteResponse = await client.DeleteAsync($"/api/products/{createdInactive!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAllResponse = await client.GetAsync("/api/products");
        var products = await getAllResponse.Content.ReadFromJsonAsync<List<ProductResponse>>();

        Assert.Equal(HttpStatusCode.OK, getAllResponse.StatusCode);
        Assert.NotNull(products);
        Assert.Contains(products!, x => x.Barcode == "SKU-GET-001");
        Assert.DoesNotContain(products!, x => x.Barcode == "SKU-GET-002");
        Assert.All(products!, x => Assert.True(x.IsActive));
    }

    [Fact]
    public async Task GetProductById_WhenProductExists_ShouldReturnProduct()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var request = CreateProductRequest("SKU-GET-BY-ID-001", "Visible Product");
        var createResponse = await client.PostAsJsonAsync("/api/products", request);
        var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createdProduct);

        var getResponse = await client.GetAsync($"/api/products/{createdProduct!.Id}");
        var fetchedProduct = await getResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(fetchedProduct);
        Assert.Equal(createdProduct.Id, fetchedProduct!.Id);
        Assert.Equal(request.Barcode, fetchedProduct.Barcode);
        Assert.Equal(request.Name, fetchedProduct.Name);
    }

    [Fact]
    public async Task PutProduct_ShouldUpdateProductValues()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var createRequest = CreateProductRequest("SKU-PUT-001", "Original Product");
        var createResponse = await client.PostAsJsonAsync("/api/products", createRequest);
        var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createdProduct);

        var updateRequest = CreateProductUpdateRequest("SKU-PUT-001-UPDATED", "Updated Product", 75.50m, 12);
        var updateResponse = await client.PutAsJsonAsync($"/api/products/{createdProduct!.Id}", updateRequest);
        var updatedProduct = await updateResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.NotNull(updatedProduct);
        Assert.Equal("SKU-PUT-001-UPDATED", updatedProduct!.Barcode);
        Assert.Equal("Updated Product", updatedProduct.Name);
        Assert.Equal(75.50m, updatedProduct.UnitPrice);
        Assert.Equal(12, updatedProduct.MinimumStock);
    }

    [Fact]
    public async Task DeleteProduct_ShouldSoftDeleteAndPersistAuditFields()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var request = CreateProductRequest("SKU-DELETE-001", "Delete Product");
        var createResponse = await client.PostAsJsonAsync("/api/products", request);
        var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createdProduct);

        var deleteResponse = await client.DeleteAsync($"/api/products/{createdProduct!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDeleteResponse = await client.GetAsync($"/api/products/{createdProduct.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var persistedProduct = await dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == createdProduct.Id);

        Assert.NotNull(persistedProduct);
        Assert.False(persistedProduct!.IsActive);
        Assert.NotNull(persistedProduct.DeactivatedAtUtc);
        Assert.NotNull(persistedProduct.UpdatedAtUtc);
        Assert.Equal(createdProduct.CreatedAtUtc, persistedProduct.CreatedAtUtc);
    }

    [Fact]
    public async Task PostProduct_WithDuplicateBarcode_ShouldReturnConflict()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var firstRequest = CreateProductRequest("SKU-DUP-001", "First Product");
        var firstResponse = await client.PostAsJsonAsync("/api/products", firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var duplicateRequest = CreateProductRequest("SKU-DUP-001", "Second Product");
        var duplicateResponse = await client.PostAsJsonAsync("/api/products", duplicateRequest);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal("application/problem+json", duplicateResponse.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await duplicateResponse.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.True(root.TryGetProperty("barcode", out var barcodeProperty));
        Assert.Equal("SKU-DUP-001", barcodeProperty.GetString());
    }

    [Fact]
    public async Task PostProduct_WithEmptyBarcodeOrName_ShouldReturnBadRequest()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var request = CreateProductRequest("", "   ");
        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostProduct_WithNegativeValues_ShouldReturnBadRequest()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var request = CreateProductRequest("SKU-NEG-001", "Negative Product", -1m, -5);
        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetProductById_WhenProductIsInactive_ShouldReturnNotFound()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var createRequest = CreateProductRequest("SKU-INACTIVE-GET-001", "Inactive Get Product");
        var createResponse = await client.PostAsJsonAsync("/api/products", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var deleteResponse = await client.DeleteAsync($"/api/products/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetProductById_WhenProductDoesNotExist_ShouldReturnNotFound()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProduct_WhenProductDoesNotExist_ShouldReturnNotFound()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", CreateProductUpdateRequest("SKU-404", "Missing Product"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

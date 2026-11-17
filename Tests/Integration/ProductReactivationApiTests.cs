using Business.Common.Security;
using System.Net;
using System.Net.Http.Json;
using Business.Features.Products.DTOs;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class ProductReactivationApiTests
{
    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, string barcode)
    {
        var response = await client.PostAsJsonAsync("/api/products", new ProductCreateRequest
        {
            Barcode = barcode,
            Name = "Reactivation test product",
            Description = "Preserve product information",
            UnitPrice = 125.50m,
            MinimumStock = 8
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        return product;
    }

    [Fact]
    public async Task GetInactive_WhenNoProductsExist_ShouldReturnEmptyList()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.GetAsync("/api/products/inactive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<List<ProductResponse>>();
        Assert.NotNull(products);
        Assert.Empty(products);
    }

    [Fact]
    public async Task GetInactive_ShouldReturnOnlyDeactivatedProducts()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var active = await CreateProductAsync(client, "REACT-ACTIVE");
        var inactive = await CreateProductAsync(client, "REACT-INACTIVE");

        var deleteResponse = await client.DeleteAsync($"/api/products/{inactive.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var response = await client.GetAsync("/api/products/inactive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<List<ProductResponse>>();
        Assert.NotNull(products);
        var product = Assert.Single(products);
        Assert.Equal(inactive.Id, product.Id);
        Assert.False(product.IsActive);
        Assert.DoesNotContain(products, item => item.Id == active.Id);

        var activeProducts = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        Assert.NotNull(activeProducts);
        Assert.Equal(active.Id, Assert.Single(activeProducts).Id);
        Assert.All(activeProducts, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task Reactivate_ShouldRestoreActiveVisibilityAndClearDeactivationAudit()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var created = await CreateProductAsync(client, "REACT-LIFECYCLE");
        var deleteResponse = await client.DeleteAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{created.Id}")).StatusCode);
        var inactiveProducts = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products/inactive");
        Assert.NotNull(inactiveProducts);
        Assert.Equal(created.Id, Assert.Single(inactiveProducts).Id);

        DateTime? lastUpdatedAtUtc;
        var deactivatedBy = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            var product = await context.Products.IgnoreQueryFilters().SingleAsync(item => item.Id == created.Id);
            Assert.NotNull(product.DeactivatedAtUtc);
            product.DeactivatedByUserId = deactivatedBy;
            await context.SaveChangesAsync();
            lastUpdatedAtUtc = product.UpdatedAtUtc;
        }

        var response = await client.PatchAsync($"/api/products/{created.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reactivated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(reactivated);
        Assert.True(reactivated.IsActive);
        Assert.Equal(created.Id, reactivated.Id);
        Assert.Equal(created.Barcode, reactivated.Barcode);
        Assert.Equal(created.Name, reactivated.Name);
        Assert.Equal(created.Description, reactivated.Description);
        Assert.Equal(created.UnitPrice, reactivated.UnitPrice);
        Assert.Equal(created.MinimumStock, reactivated.MinimumStock);
        Assert.Equal(created.CreatedAtUtc, reactivated.CreatedAtUtc);
        Assert.NotNull(reactivated.UpdatedAtUtc);
        Assert.True(reactivated.UpdatedAtUtc > lastUpdatedAtUtc);

        var activeProducts = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        Assert.NotNull(activeProducts);
        Assert.Equal(created.Id, Assert.Single(activeProducts).Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/products/{created.Id}")).StatusCode);
        var remainingInactive = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products/inactive");
        Assert.NotNull(remainingInactive);
        Assert.Empty(remainingInactive);

        using var verificationScope = factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var persisted = await dbContext.Products.IgnoreQueryFilters().AsNoTracking().SingleAsync();
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
    public async Task Reactivate_WhenAlreadyActive_ShouldReturnConflictWithoutChangingProduct()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var created = await CreateProductAsync(client, "REACT-ALREADY-ACTIVE");

        var response = await client.PatchAsync($"/api/products/{created.Id}/reactivate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Ürün zaten aktif", problem.Title);
        var persisted = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{created.Id}");
        Assert.NotNull(persisted);
        Assert.True(persisted.IsActive);
        Assert.Equal(created.UpdatedAtUtc, persisted.UpdatedAtUtc);
    }

    [Fact]
    public async Task Reactivate_WhenMissing_ShouldReturnNotFound()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.PatchAsync($"/api/products/{Guid.NewGuid()}/reactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task InactiveBarcode_ShouldRemainReservedBeforeAndAfterReactivation()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var original = await CreateProductAsync(client, "REACT-RESERVED");
        var other = await CreateProductAsync(client, "REACT-OTHER");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{original.Id}")).StatusCode);

        var duplicateRequest = new ProductCreateRequest { Barcode = original.Barcode, Name = "Duplicate", UnitPrice = 1, MinimumStock = 0 };
        var duplicateCreate = await client.PostAsJsonAsync("/api/products", duplicateRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateCreate.StatusCode);
        var duplicateUpdate = await client.PutAsJsonAsync($"/api/products/{other.Id}", new ProductUpdateRequest
        {
            Barcode = original.Barcode, Name = other.Name, UnitPrice = other.UnitPrice, MinimumStock = other.MinimumStock
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateUpdate.StatusCode);

        var reactivate = await client.PatchAsync($"/api/products/{original.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        var duplicateAfterReactivation = await client.PostAsJsonAsync("/api/products", duplicateRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateAfterReactivation.StatusCode);
        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        Assert.NotNull(products);
        Assert.Equal(2, products.Count);
        Assert.Single(products, item => item.Barcode == original.Barcode);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Business.Common.Security;
using Business.Features.Products;
using Business.Features.Products.DTOs;
using DataAccess.Identity;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Contracts.Auth;

namespace Tests.Integration;

public class PackageFGTests : IDisposable
{
    private readonly CustomWebApplicationFactory factory = new();

    [Theory]
    [InlineData("PRD-001", "InternalIdentifier")]
    [InlineData("000123", "InternalIdentifier")]
    [InlineData("96385074", "GtinChecksumValid")]
    [InlineData("036000291452", "GtinChecksumValid")]
    [InlineData("4006381333931", "GtinChecksumValid")]
    [InlineData("00036000291452", "GtinChecksumValid")]
    [InlineData("4006381333932", "GtinChecksumInvalid")]
    [InlineData("00000000", "GtinChecksumInvalid")]
    [InlineData("４００６３８１３３３９３１", "InternalIdentifier")]
    public void BarcodeAssessment_DistinguishesLegacyIdsFromGtinSyntax(string value, string expected)
        => Assert.Equal(expected, BarcodeAssessment.Assess(value));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public async Task InvalidBarcode_CreateAndUpdateReturn400(string? barcode)
    {
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var payload = new { barcode, name = "Invalid barcode", unitPrice = 1, minimumStock = 0 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", payload)).StatusCode);
    }

    [Fact]
    public async Task LegacyProduct_CanReceiveRealBarcodeWithoutChangingIdAuditOrStockRelations()
    {
        Guid id, inventoryId, movementId;
        var createdAt = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var product = new Product { Barcode = "PRD-001", Name = "Legacy", UnitPrice = 1, CreatedAtUtc = createdAt };
            var warehouse = new Warehouse { Code = "LEGACY", Name = "Legacy warehouse" };
            db.AddRange(product, warehouse);
            await db.SaveChangesAsync();
            var inventory = new Inventory { ProductId = product.Id, WarehouseId = warehouse.Id, Quantity = 5, Version = Guid.NewGuid() };
            var movement = new StockMovement { Inventory = inventory,
                Quantity = 5, QuantityBefore = 0, QuantityAfter = 5, MovementType = MovementType.StockIn };
            db.AddRange(inventory, movement);
            await db.SaveChangesAsync();
            (id, inventoryId, movementId) = (product.Id, inventory.Id, movement.Id);
            createdAt = product.CreatedAtUtc;
        }
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var original = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{id}");
        Assert.Equal("PRD-001", original!.Barcode);
        Assert.Equal("InternalIdentifier", original.BarcodeFormat);
        var response = await client.PutAsJsonAsync($"/api/products/{id}", new {
            barcode = "00036000291452", name = "Legacy", unitPrice = 1, minimumStock = 0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("00036000291452", json.RootElement.GetProperty("barcode").GetString());
        Assert.False(json.RootElement.TryGetProperty("sku", out _));
        var updated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal(id, updated!.Id);
        using var verification = factory.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(id, (await context.Inventories.SingleAsync(x => x.Id == inventoryId)).ProductId);
        Assert.Equal(inventoryId, (await context.StockMovements.SingleAsync(x => x.Id == movementId)).InventoryId);
        Assert.Equal(createdAt, (await context.Products.SingleAsync(x => x.Id == id)).CreatedAtUtc);
    }

    public static IEnumerable<object[]> CustomerDeniedRoutes()
    {
        foreach (var catalog in new[] { "products", "warehouses", "customers" })
        {
            yield return ["GET", $"/api/{catalog}"];
            yield return ["GET", $"/api/{catalog}/inactive"];
            yield return ["GET", $"/api/{catalog}/{Guid.Empty}"];
            foreach (var method in new[] { "POST", "PUT", "DELETE", "PATCH" })
                yield return [method, method == "POST" ? $"/api/{catalog}" : method == "PATCH"
                    ? $"/api/{catalog}/{Guid.Empty}/reactivate" : $"/api/{catalog}/{Guid.Empty}"];
        }
        foreach (var path in new[] { "stock", "stock/movements", "stock/low-stock" }) yield return ["GET", "/api/" + path];
        yield return ["POST", "/api/stock/in"];
        yield return ["POST", "/api/stock/out"];
    }

    [Theory]
    [MemberData(nameof(CustomerDeniedRoutes))]
    public async Task Customer_CannotAccessManagementApis(string method, string path)
    {
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Customer);
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData(ApplicationRoles.Admin)]
    [InlineData(ApplicationRoles.WarehouseStaff)]
    [InlineData(ApplicationRoles.Viewer)]
    public async Task OperationalRoles_CannotEnterCustomerPortal(string role)
    {
        using var client = await factory.CreateRoleClientAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customer-portal/me")).StatusCode);
    }

    [Fact]
    public async Task CustomerPortal_ResolvesOnlyOwnedActiveCustomerAndRejectsRevokedRole()
    {
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Customer);
        var user = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customer-portal/me")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var owned = new Customer { Code = "OWN", Name = "Owned customer" };
        var other = new Customer { Code = "OTHER", Name = "Other customer" };
        db.AddRange(owned, other);
        await db.SaveChangesAsync();
        var account = await db.Users.SingleAsync(x => x.Id == user.Id);
        account.CustomerId = owned.Id;
        await db.SaveChangesAsync();
        var response = await client.GetAsync($"/api/customer-portal/me?customerId={other.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Owned customer", json.RootElement.GetProperty("name").GetString());
        Assert.Single(json.RootElement.EnumerateObject());

        using var otherClient = await factory.CreateRoleClientAsync(ApplicationRoles.Customer, freshSession: true);
        var otherUser = (await otherClient.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        var otherAccount = await db.Users.SingleAsync(x => x.Id == otherUser.Id);
        otherAccount.CustomerId = other.Id;
        await db.SaveChangesAsync();
        var otherResponse = await otherClient.GetAsync($"/api/customer-portal/me?customerId={owned.Id}");
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        using var otherJson = JsonDocument.Parse(await otherResponse.Content.ReadAsStringAsync());
        Assert.Equal("Other customer", otherJson.RootElement.GetProperty("name").GetString());

        owned.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customer-portal/me")).StatusCode);
        owned.IsActive = true;
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.RemoveFromRoleAsync(account, ApplicationRoles.Customer)).Succeeded);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customer-portal/me")).StatusCode);
    }

    [Fact]
    public async Task CustomerLink_DatabaseRejectsDuplicateAndMissingCustomer()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var customer = new Customer { Code = "LINK", Name = "Linked" };
        db.Add(customer);
        await db.SaveChangesAsync();
        db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), CustomerId = customer.Id });
        await db.SaveChangesAsync();
        // Simulate a separate request: tracked one-to-one fixup would unlink the first account.
        db.ChangeTracker.Clear();
        db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), CustomerId = customer.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    public void Dispose() => factory.Dispose();
}

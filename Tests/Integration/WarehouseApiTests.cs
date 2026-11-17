using Business.Common.Security;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Business.Features.Warehouses.DTOs;
using DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class WarehouseApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public WarehouseApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static WarehouseCreateRequest CreateWarehouseRequest(string code, string name, string? location = null)
    {
        return new WarehouseCreateRequest
        {
            Code = code,
            Name = name,
            Location = location
        };
    }

    private static WarehouseUpdateRequest CreateWarehouseUpdateRequest(string code, string name, string? location = "Istanbul")
    {
        return new WarehouseUpdateRequest
        {
            Code = code,
            Name = name,
            Location = location
        };
    }

    [Fact]
    public async Task PostWarehouse_ShouldCreateWarehouseAndReturnCreatedStatus()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var request = CreateWarehouseRequest("WH-POST-001", "Main Warehouse", "Ankara");

        var postResponse = await client.PostAsJsonAsync("/api/warehouses", request);
        var createdWarehouse = await postResponse.Content.ReadFromJsonAsync<WarehouseResponse>();

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        Assert.NotNull(createdWarehouse);
        Assert.NotEqual(Guid.Empty, createdWarehouse!.Id);
        Assert.Equal(request.Code, createdWarehouse.Code);
        Assert.Equal(request.Name, createdWarehouse.Name);
        Assert.True(createdWarehouse.IsActive);
        Assert.NotEqual(default, createdWarehouse.CreatedAtUtc);
        Assert.Null(createdWarehouse.UpdatedAtUtc);
        Assert.NotNull(postResponse.Headers.Location);
    }

    [Fact]
    public async Task GetWarehouses_ShouldReturnOnlyActiveWarehouses()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var activeRequest = CreateWarehouseRequest("WH-GET-001", "Active Warehouse", "Izmir");
        var activeResponse = await client.PostAsJsonAsync("/api/warehouses", activeRequest);
        Assert.Equal(HttpStatusCode.Created, activeResponse.StatusCode);

        var inactiveRequest = CreateWarehouseRequest("WH-GET-002", "Inactive Warehouse", "Bursa");
        var inactiveResponse = await client.PostAsJsonAsync("/api/warehouses", inactiveRequest);
        Assert.Equal(HttpStatusCode.Created, inactiveResponse.StatusCode);
        var inactiveWarehouse = await inactiveResponse.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(inactiveWarehouse);

        var deleteResponse = await client.DeleteAsync($"/api/warehouses/{inactiveWarehouse!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAllResponse = await client.GetAsync("/api/warehouses");
        var warehouses = await getAllResponse.Content.ReadFromJsonAsync<List<WarehouseResponse>>();

        Assert.Equal(HttpStatusCode.OK, getAllResponse.StatusCode);
        Assert.NotNull(warehouses);
        Assert.Contains(warehouses!, x => x.Code == "WH-GET-001");
        Assert.DoesNotContain(warehouses!, x => x.Code == "WH-GET-002");
        Assert.All(warehouses!, x => Assert.True(x.IsActive));
    }

    [Fact]
    public async Task GetWarehouseById_WhenWarehouseExists_ShouldReturnWarehouse()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var request = CreateWarehouseRequest("WH-GET-BY-ID-001", "Detailed Warehouse", "Antalya");
        var createResponse = await client.PostAsJsonAsync("/api/warehouses", request);
        var createdWarehouse = await createResponse.Content.ReadFromJsonAsync<WarehouseResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createdWarehouse);

        var getResponse = await client.GetAsync($"/api/warehouses/{createdWarehouse!.Id}");
        var fetchedWarehouse = await getResponse.Content.ReadFromJsonAsync<WarehouseResponse>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(fetchedWarehouse);
        Assert.Equal(createdWarehouse.Id, fetchedWarehouse!.Id);
        Assert.Equal(request.Code, fetchedWarehouse.Code);
        Assert.Equal(request.Name, fetchedWarehouse.Name);
    }

    [Fact]
    public async Task PutWarehouse_ShouldUpdateWarehouseValues()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var createRequest = CreateWarehouseRequest("WH-PUT-001", "Original Warehouse", "Original Location");
        var createResponse = await client.PostAsJsonAsync("/api/warehouses", createRequest);
        var createdWarehouse = await createResponse.Content.ReadFromJsonAsync<WarehouseResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createdWarehouse);

        var updateRequest = CreateWarehouseUpdateRequest("WH-PUT-001-UPDATED", "Updated Warehouse", "Updated Location");
        var updateResponse = await client.PutAsJsonAsync($"/api/warehouses/{createdWarehouse!.Id}", updateRequest);
        var updatedWarehouse = await updateResponse.Content.ReadFromJsonAsync<WarehouseResponse>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.NotNull(updatedWarehouse);
        Assert.Equal("WH-PUT-001-UPDATED", updatedWarehouse!.Code);
        Assert.Equal("Updated Warehouse", updatedWarehouse.Name);
        Assert.Equal("Updated Location", updatedWarehouse.Location);
    }

    [Fact]
    public async Task DeleteWarehouse_ShouldSoftDeleteAndPersistAuditFields()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var request = CreateWarehouseRequest("WH-DELETE-001", "Delete Warehouse", "Konya");
        var createResponse = await client.PostAsJsonAsync("/api/warehouses", request);
        var createdWarehouse = await createResponse.Content.ReadFromJsonAsync<WarehouseResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createdWarehouse);

        var deleteResponse = await client.DeleteAsync($"/api/warehouses/{createdWarehouse!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDeleteResponse = await client.GetAsync($"/api/warehouses/{createdWarehouse.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var persistedWarehouse = await dbContext.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == createdWarehouse.Id);

        Assert.NotNull(persistedWarehouse);
        Assert.False(persistedWarehouse!.IsActive);
        Assert.NotNull(persistedWarehouse.DeactivatedAtUtc);
        Assert.NotNull(persistedWarehouse.UpdatedAtUtc);
        Assert.Equal(createdWarehouse.CreatedAtUtc, persistedWarehouse.CreatedAtUtc);
    }

    [Fact]
    public async Task PostWarehouse_WithDuplicateCode_ShouldReturnConflict()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var firstRequest = CreateWarehouseRequest("WH-DUP-001", "First Warehouse");
        var firstResponse = await client.PostAsJsonAsync("/api/warehouses", firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var duplicateRequest = CreateWarehouseRequest("WH-DUP-001", "Second Warehouse");
        var duplicateResponse = await client.PostAsJsonAsync("/api/warehouses", duplicateRequest);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal("application/problem+json", duplicateResponse.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await duplicateResponse.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.True(root.TryGetProperty("code", out var codeProperty));
        Assert.Equal("WH-DUP-001", codeProperty.GetString());
    }

    [Fact]
    public async Task PostWarehouse_WithEmptyCode_ShouldReturnBadRequest()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.PostAsJsonAsync("/api/warehouses", new WarehouseCreateRequest { Code = "", Name = "Warehouse" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostWarehouse_WithEmptyName_ShouldReturnBadRequest()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.PostAsJsonAsync("/api/warehouses", new WarehouseCreateRequest { Code = "WH-EMPTY-NAME", Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostWarehouse_WithTooLongLocation_ShouldReturnBadRequest()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.PostAsJsonAsync("/api/warehouses", new WarehouseCreateRequest
        {
            Code = "WH-LONG-LOCATION",
            Name = "Warehouse",
            Location = new string('x', 301)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWarehouseById_WhenWarehouseDoesNotExist_ShouldReturnNotFound()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.GetAsync($"/api/warehouses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWarehouseById_WhenWarehouseIsInactive_ShouldReturnNotFound()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var createResponse = await client.PostAsJsonAsync("/api/warehouses", CreateWarehouseRequest("WH-INACTIVE-GET", "Inactive Warehouse"));
        var createdWarehouse = await createResponse.Content.ReadFromJsonAsync<WarehouseResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createdWarehouse);

        var deleteResponse = await client.DeleteAsync($"/api/warehouses/{createdWarehouse!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/warehouses/{createdWarehouse.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateWarehouse_WhenWarehouseDoesNotExist_ShouldReturnNotFound()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.PutAsJsonAsync($"/api/warehouses/{Guid.NewGuid()}", CreateWarehouseUpdateRequest("WH-MISSING", "Missing Warehouse"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

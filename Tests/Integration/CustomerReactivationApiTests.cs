using Business.Common.Security;
using System.Net;
using System.Net.Http.Json;
using Business.Features.Customers.DTOs;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class CustomerReactivationApiTests
{
    private static async Task<CustomerResponse> CreateCustomerAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/customers", new CustomerCreateRequest
        {
            Code = code,
            Name = "Reactivation test customer",
            Email = "reactivation@example.test",
            Phone = "03120000000",
            TaxNumber = "1234567890",
            Address = "Test address"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = await response.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.NotNull(customer);
        return customer;
    }

    [Fact]
    public async Task GetInactive_WhenNoCustomersExist_ShouldReturnEmptyList()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.GetAsync("/api/customers/inactive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var customers = await response.Content.ReadFromJsonAsync<List<CustomerResponse>>();
        Assert.NotNull(customers);
        Assert.Empty(customers);
    }

    [Fact]
    public async Task GetInactive_ShouldSeparateActiveAndDeactivatedCustomers()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var active = await CreateCustomerAsync(client, "REACT-ACTIVE");
        var inactive = await CreateCustomerAsync(client, "REACT-INACTIVE");

        var deleteResponse = await client.DeleteAsync($"/api/customers/{inactive.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var response = await client.GetAsync("/api/customers/inactive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var customers = await response.Content.ReadFromJsonAsync<List<CustomerResponse>>();
        Assert.NotNull(customers);
        var customer = Assert.Single(customers);
        Assert.Equal(inactive.Id, customer.Id);
        Assert.False(customer.IsActive);
        Assert.DoesNotContain(customers, item => item.Id == active.Id);

        var activeCustomers = await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers");
        Assert.NotNull(activeCustomers);
        Assert.Equal(active.Id, Assert.Single(activeCustomers).Id);
        Assert.All(activeCustomers, item => Assert.True(item.IsActive));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/{inactive.Id}")).StatusCode);
    }

    [Fact]
    public async Task Reactivate_ShouldRestoreVisibilityAndPreserveCustomerAndCreationAudit()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var createdBy = Guid.NewGuid();
        var original = new Customer
        {
            Code = "REACT-LIFECYCLE",
            Name = "Preserved customer",
            Email = "preserved@example.test",
            Phone = "03120000001",
            TaxNumber = "9876543210",
            Address = "Preserved test address",
            CreatedByUserId = createdBy
        };
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            context.Customers.Add(original);
            await context.SaveChangesAsync();
        }

        var deleteResponse = await client.DeleteAsync($"/api/customers/{original.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var inactive = await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers/inactive");
        Assert.NotNull(inactive);
        Assert.Equal(original.Id, Assert.Single(inactive).Id);
        var activeBefore = await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers");
        Assert.NotNull(activeBefore);
        Assert.Empty(activeBefore);

        DateTime? lastUpdatedAtUtc;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var customer = await context.Customers.IgnoreQueryFilters().SingleAsync();
            Assert.NotNull(customer.DeactivatedAtUtc);
            customer.DeactivatedByUserId = Guid.NewGuid();
            await context.SaveChangesAsync();
            lastUpdatedAtUtc = customer.UpdatedAtUtc;
            Assert.NotNull(lastUpdatedAtUtc);
            Assert.NotNull(customer.DeactivatedByUserId);
        }

        var response = await client.PatchAsync($"/api/customers/{original.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reactivated = await response.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.NotNull(reactivated);
        Assert.True(reactivated.IsActive);
        Assert.Equal(original.Id, reactivated.Id);
        Assert.Equal(original.Code, reactivated.Code);
        Assert.Equal(original.Name, reactivated.Name);
        Assert.Equal(original.Email, reactivated.Email);
        Assert.Equal(original.Phone, reactivated.Phone);
        Assert.Equal(original.TaxNumber, reactivated.TaxNumber);
        Assert.Equal(original.Address, reactivated.Address);
        Assert.Equal(original.CreatedAtUtc, reactivated.CreatedAtUtc);
        Assert.NotNull(reactivated.UpdatedAtUtc);
        Assert.True(reactivated.UpdatedAtUtc > lastUpdatedAtUtc);

        var activeAfter = await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers");
        Assert.NotNull(activeAfter);
        Assert.Equal(original.Id, Assert.Single(activeAfter).Id);
        var inactiveAfter = await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers/inactive");
        Assert.NotNull(inactiveAfter);
        Assert.Empty(inactiveAfter);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/customers/{original.Id}")).StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var persisted = await dbContext.Customers.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Null(persisted.DeactivatedAtUtc);
        Assert.Null(persisted.DeactivatedByUserId);
        Assert.Equal(createdBy, persisted.CreatedByUserId);
        Assert.Equal(original.CreatedAtUtc, persisted.CreatedAtUtc);
        Assert.Equal(reactivated.UpdatedAtUtc, persisted.UpdatedAtUtc);
        Assert.Equal(original.Code, persisted.Code);
        Assert.Equal(original.Name, persisted.Name);
        Assert.Equal(original.Email, persisted.Email);
        Assert.Equal(original.Phone, persisted.Phone);
        Assert.Equal(original.TaxNumber, persisted.TaxNumber);
        Assert.Equal(original.Address, persisted.Address);
    }

    [Fact]
    public async Task Reactivate_WhenAlreadyActive_ShouldReturnConflictWithoutChangingCustomer()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        var created = await CreateCustomerAsync(client, "REACT-ALREADY-ACTIVE");

        var response = await client.PatchAsync($"/api/customers/{created.Id}/reactivate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Müşteri zaten aktif", problem.Title);
        Assert.Equal("Bu müşteri zaten aktif olduğu için yeniden aktifleştirilemez.", problem.Detail);
        var persisted = await client.GetFromJsonAsync<CustomerResponse>($"/api/customers/{created.Id}");
        Assert.NotNull(persisted);
        Assert.True(persisted.IsActive);
        Assert.Equal(created.UpdatedAtUtc, persisted.UpdatedAtUtc);
    }

    [Fact]
    public async Task Reactivate_WhenMissing_ShouldReturnNotFound()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = await factory.CreateRoleClientAsync(ApplicationRoles.Admin);

        var response = await client.PatchAsync($"/api/customers/{Guid.NewGuid()}/reactivate", null);

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
        var original = await CreateCustomerAsync(client, "REACT-RESERVED");
        var other = await CreateCustomerAsync(client, "REACT-OTHER");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/customers/{original.Id}")).StatusCode);

        var duplicateRequest = new CustomerCreateRequest { Code = original.Code, Name = "Duplicate customer" };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/customers", duplicateRequest)).StatusCode);
        var duplicateUpdate = await client.PutAsJsonAsync($"/api/customers/{other.Id}", new CustomerUpdateRequest
        {
            Code = original.Code, Name = other.Name, Email = other.Email, Phone = other.Phone,
            TaxNumber = other.TaxNumber, Address = other.Address
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateUpdate.StatusCode);

        var reactivateResponse = await client.PatchAsync($"/api/customers/{original.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/customers", duplicateRequest)).StatusCode);
        var customers = await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers");
        Assert.NotNull(customers);
        Assert.Equal(2, customers.Count);
        Assert.Single(customers, item => item.Code == original.Code);
    }
}

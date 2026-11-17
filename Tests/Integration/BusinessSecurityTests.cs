using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Business.Common.Security;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebAPI.Controllers;
using WebAPI.Infrastructure.Security;

namespace Tests.Integration;

public class BusinessSecurityTests : IDisposable
{
    private readonly SecurityTestFactory _factory = new();
    private static readonly string[] Roles =
        [ApplicationRoles.Admin, ApplicationRoles.WarehouseStaff, ApplicationRoles.Viewer];
    private static readonly string[] Catalogs = ["products", "warehouses", "customers"];
    private static readonly string[] Verbs = ["POST", "PUT", "PATCH", "DELETE"];

    public static IEnumerable<object[]> ReadCases()
    {
        foreach (var role in Roles)
        foreach (var path in new[] { "products", "warehouses", "stock", "stock/movements", "stock/low-stock" })
            yield return [role, path];
    }

    public static IEnumerable<object[]> ForbiddenCatalogWrites()
    {
        foreach (var role in Roles.Skip(1))
        foreach (var catalog in Catalogs)
        foreach (var verb in Verbs) yield return [role, catalog, verb];
    }

    public static IEnumerable<object[]> CsrfWrites()
    {
        foreach (var invalid in new[] { false, true })
        {
            foreach (var catalog in Catalogs)
            foreach (var verb in Verbs) yield return [catalog, verb, invalid];
            yield return ["stock/in", "POST", invalid];
            yield return ["stock/out", "POST", invalid];
        }
    }

    [Theory]
    [InlineData("products")]
    [InlineData("warehouses")]
    [InlineData("customers")]
    [InlineData("stock")]
    [InlineData("stock/movements")]
    [InlineData("stock/low-stock")]
    public async Task AnonymousReads_ShouldReturn401WithoutRedirect(string path)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/" + path);
        AssertDenied(response, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(ReadCases))]
    public async Task AuthorizedRoles_ShouldReadActiveOperationalDataWithoutCsrfHeader(string role, string path)
    {
        using var client = await _factory.CreateRoleClientAsync(role, withCsrf: false);
        using var response = await client.GetAsync("/api/" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousStockOut_ShouldReturn401AndLeaveBusinessDataUnchanged()
    {
        var seed = await SeedAsync();
        var before = await SnapshotAsync();
        using var client = _factory.CreateClient();
        using var request = Mutation(seed, "stock/out", "POST");
        using var response = await client.SendAsync(request);
        AssertDenied(response, HttpStatusCode.Unauthorized);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task WarehouseStaff_ShouldPerformStockInAndOut()
    {
        var seed = await SeedAsync();
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.WarehouseStaff);
        foreach (var path in new[] { "stock/in", "stock/out" })
        {
            using var request = Mutation(seed, path, "POST");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(10, (await context.Inventories.SingleAsync()).Quantity);
        Assert.Equal(2, await context.StockMovements.CountAsync());
    }

    [Fact]
    public async Task ViewerStockOut_ShouldReturn403AndLeaveBusinessDataUnchanged()
    {
        var seed = await SeedAsync();
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Viewer);
        var before = await SnapshotAsync();
        using var request = Mutation(seed, "stock/out", "POST");
        using var response = await client.SendAsync(request);
        AssertDenied(response, HttpStatusCode.Forbidden);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(ForbiddenCatalogWrites))]
    public async Task NonAdminCatalogWrites_ShouldReturn403AndNotChangeData(string role, string catalog, string verb)
    {
        var seed = await SeedAsync(verb == "PATCH" ? catalog : null);
        using var client = await _factory.CreateRoleClientAsync(role);
        var before = await SnapshotAsync();
        using var request = Mutation(seed, catalog, verb);
        using var response = await client.SendAsync(request);
        AssertDenied(response, HttpStatusCode.Forbidden);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(ApplicationRoles.WarehouseStaff)]
    [InlineData(ApplicationRoles.Viewer)]
    public async Task NonAdmin_ShouldNotReadAnyCustomerData(string role)
    {
        var seed = await SeedAsync();
        using var client = await _factory.CreateRoleClientAsync(role);
        foreach (var path in new[] { "customers", "customers/" + seed.CustomerId, "customers/inactive" })
        {
            using var response = await client.GetAsync("/api/" + path);
            AssertDenied(response, HttpStatusCode.Forbidden);
        }
    }

    [Theory]
    [InlineData(ApplicationRoles.Admin, "products", true)]
    [InlineData(ApplicationRoles.Admin, "warehouses", true)]
    [InlineData(ApplicationRoles.Admin, "customers", true)]
    [InlineData(ApplicationRoles.WarehouseStaff, "products", false)]
    [InlineData(ApplicationRoles.WarehouseStaff, "warehouses", false)]
    [InlineData(ApplicationRoles.WarehouseStaff, "customers", false)]
    [InlineData(ApplicationRoles.Viewer, "products", false)]
    [InlineData(ApplicationRoles.Viewer, "warehouses", false)]
    [InlineData(ApplicationRoles.Viewer, "customers", false)]
    public async Task InactiveLists_ShouldBeAdminOnly(string role, string catalog, bool allowed)
    {
        using var client = await _factory.CreateRoleClientAsync(role, withCsrf: false);
        using var response = await client.GetAsync("/api/" + catalog + "/inactive");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(CsrfWrites))]
    public async Task AdminWrites_WithMissingOrInvalidCsrf_ShouldReturn400AndNotChangeData(
        string resource, string verb, bool invalid)
    {
        var seed = await SeedAsync(verb == "PATCH" ? resource : null);
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin, withCsrf: false);
        if (invalid) client.DefaultRequestHeaders.Add(AuthenticationConfiguration.CsrfHeader, "invalid-token");
        var before = await SnapshotAsync();
        using var request = Mutation(seed, resource, verb);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task NewEndpointWithoutAuthorize_ShouldStillRequireAuthentication()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/security-test/unannotated");
        AssertDenied(response, HttpStatusCode.Unauthorized);
        using var authenticated = await _factory.CreateRoleClientAsync(ApplicationRoles.Viewer, withCsrf: false);
        using var allowed = await authenticated.GetAsync("/api/security-test/unannotated");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task Health_ShouldBeExplicitlyPublic()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void ProductionEndpoints_ShouldHavePoliciesAndOnlyExplicitPublicExceptions()
    {
        var services = _factory.Services;
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly
                == typeof(AuthController).Assembly).ToArray();
        Assert.NotEmpty(endpoints);
        var options = services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy.Requirements,
            requirement => requirement is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        var publicRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "api/auth/login", "api/auth/logout", "api/auth/csrf", "api/health" };
        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText!.TrimStart('/');
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (anonymous) { Assert.True(publicRoutes.Contains(route), "Unexpected anonymous API endpoint."); continue; }
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(metadata => metadata.Policy).Where(policy => !string.IsNullOrEmpty(policy)).ToArray();
            if (action.ControllerTypeInfo == typeof(AuthController))
            {
                Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
                continue;
            }
            Assert.NotEmpty(policies);
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods;
            if (methods.Any(method => method is "POST" or "PUT" or "PATCH" or "DELETE"))
                Assert.Contains(policies, policy => policy!.EndsWith(".Manage", StringComparison.Ordinal)
                    || policy is ApplicationPolicies.StockIn or ApplicationPolicies.StockOut
                    || (action.ControllerTypeInfo == typeof(CustomerShoppingController)
                        && policy == ApplicationPolicies.CustomerShopping));
            if (action.ControllerTypeInfo == typeof(CustomerShoppingController))
            {
                var shoppingPolicy = options.GetPolicy(ApplicationPolicies.CustomerShopping)!;
                Assert.Contains(shoppingPolicy.Requirements, requirement => requirement is LinkedCustomerRequirement);
                var roleRequirement = Assert.Single(shoppingPolicy.Requirements
                    .OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>());
                Assert.Equal(new[] { ApplicationRoles.Customer }, roleRequirement.AllowedRoles);
            }
        }
        var mvc = services.GetRequiredService<IOptions<MvcOptions>>().Value;
        Assert.Contains(mvc.Filters, filter => filter is TypeFilterAttribute type
            && type.ImplementationType == typeof(ValidateCsrfFilter));
    }

    private static void AssertDenied(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed record Seed(Guid ProductId, Guid WarehouseId, Guid CustomerId);

    private async Task<Seed> SeedAsync(string? inactive = null)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..16];
        var product = new Product { Id = Guid.NewGuid(), Barcode = "P" + suffix, Name = "Seed product",
            UnitPrice = 10, MinimumStock = 1, IsActive = inactive != "products" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = "W" + suffix, Name = "Seed warehouse",
            IsActive = inactive != "warehouses" };
        var customer = new Customer { Id = Guid.NewGuid(), Code = "C" + suffix, Name = "Seed customer",
            IsActive = inactive != "customers" };
        context.AddRange(product, warehouse, customer);
        context.Inventories.Add(new Inventory { Id = Guid.NewGuid(), ProductId = product.Id,
            WarehouseId = warehouse.Id, Quantity = 10, Version = Guid.NewGuid() });
        await context.SaveChangesAsync();
        return new(product.Id, warehouse.Id, customer.Id);
    }

    private static HttpRequestMessage Mutation(Seed seed, string resource, string verb)
    {
        var id = resource switch { "products" => seed.ProductId, "warehouses" => seed.WarehouseId, _ => seed.CustomerId };
        var path = "/api/" + resource;
        if (verb != "POST") path += "/" + id + (verb == "PATCH" ? "/reactivate" : string.Empty);
        var request = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb is "POST" or "PUT")
        {
            var suffix = Guid.NewGuid().ToString("N")[..16];
            object body = resource switch
            {
                "products" => new { barcode = "NEWP" + suffix, name = "Changed product", unitPrice = 20, minimumStock = 2 },
                "warehouses" => new { code = "NEWW" + suffix, name = "Changed warehouse", location = "Test location" },
                "customers" => new { code = "NEWC" + suffix, name = "Changed customer" },
                _ => new { productId = seed.ProductId, warehouseId = seed.WarehouseId, quantity = 2, note = "Security test" }
            };
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    private async Task<string> SnapshotAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var snapshot = new
        {
            Products = await context.Products.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Warehouses = await context.Warehouses.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Customers = await context.Customers.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Inventories = await context.Inventories.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Movements = await context.StockMovements.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
    }

    public void Dispose() => _factory.Dispose();

    private sealed class SecurityTestFactory : CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddControllers()
                .AddApplicationPart(typeof(FallbackSecurityProbeController).Assembly));
        }
    }
}

[ApiController]
[Route("api/security-test/unannotated")]
public class FallbackSecurityProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}

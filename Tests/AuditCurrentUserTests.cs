using Business.Common.Security;
using DataAccess;
using DataAccess.Interceptors;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class AuditCurrentUserTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private sealed class Actor(Guid? userId) : ICurrentUser { public Guid? UserId { get; set; } = userId; }

    public AuditCurrentUserTests() => _connection.Open();

    private InventoryDbContext Context(ICurrentUser? actor = null)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(actor)).Options;
        var context = new InventoryDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static Task Save(InventoryDbContext context, bool async)
    {
        if (async) return context.SaveChangesAsync();
        context.SaveChanges();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateUpdateDeleteReactivate_ShouldPersistActorAndProtectCreationAudit(bool async)
    {
        var creator = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var actor = new Actor(creator);
        await using var context = Context(actor);
        var customer = new Customer { Code = "AUDIT-LIFECYCLE", Name = "Initial" };
        context.Customers.Add(customer);
        await Save(context, async);
        var createdAt = customer.CreatedAtUtc;
        Assert.Equal(creator, customer.CreatedByUserId);
        Assert.Null(customer.UpdatedByUserId);
        Assert.Null(customer.UpdatedAtUtc);
        Assert.Null(customer.DeactivatedByUserId);

        // The same interceptor must read the actor anew on every save.
        actor.UserId = editor;
        customer.Name = "Updated";
        customer.CreatedByUserId = Guid.NewGuid();
        customer.CreatedAtUtc = createdAt.AddYears(-10);
        await Save(context, async);
        context.ChangeTracker.Clear();
        customer = await context.Customers.SingleAsync();
        Assert.Equal(creator, customer.CreatedByUserId);
        Assert.Equal(createdAt, customer.CreatedAtUtc);
        Assert.Equal(editor, customer.UpdatedByUserId);
        var updatedAt = customer.UpdatedAtUtc;
        Assert.NotNull(updatedAt);

        actor.UserId = creator;
        customer.Name = "Must not be persisted by soft delete";
        customer.CreatedByUserId = Guid.NewGuid();
        customer.CreatedAtUtc = createdAt.AddYears(-10);
        context.Customers.Remove(customer);
        await Save(context, async);
        context.ChangeTracker.Clear();
        customer = await context.Customers.IgnoreQueryFilters().SingleAsync();
        Assert.False(customer.IsActive);
        Assert.Equal("Updated", customer.Name);
        Assert.Equal(creator, customer.CreatedByUserId);
        Assert.Equal(createdAt, customer.CreatedAtUtc);
        Assert.Equal(creator, customer.DeactivatedByUserId);
        Assert.Equal(creator, customer.UpdatedByUserId);
        Assert.NotNull(customer.DeactivatedAtUtc);
        Assert.True(customer.UpdatedAtUtc >= updatedAt);

        actor.UserId = editor;
        customer.IsActive = true;
        customer.DeactivatedAtUtc = null;
        customer.DeactivatedByUserId = null;
        await Save(context, async);
        context.ChangeTracker.Clear();
        customer = await context.Customers.SingleAsync();
        Assert.Equal(creator, customer.CreatedByUserId);
        Assert.Equal(createdAt, customer.CreatedAtUtc);
        Assert.Equal(editor, customer.UpdatedByUserId);
        Assert.Null(customer.DeactivatedAtUtc);
        Assert.Null(customer.DeactivatedByUserId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullOrEmptyActor_ShouldPreserveExistingAttributionWithoutFabricatingIdentity(bool empty)
    {
        var creator = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var actor = new Actor(empty ? Guid.Empty : null);
        await using var context = Context(actor);
        var attributed = new Customer { Code = "ATTRIBUTED", Name = "Before", CreatedByUserId = creator, UpdatedByUserId = editor };
        var legacy = new Customer { Code = "LEGACY", Name = "Legacy" };
        context.Customers.AddRange(attributed, legacy);
        await context.SaveChangesAsync();
        var createdAt = attributed.CreatedAtUtc;
        attributed.Name = "After";
        legacy.Name = "Changed legacy";
        await context.SaveChangesAsync();
        context.Customers.Remove(attributed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persisted = await context.Customers.IgnoreQueryFilters().SingleAsync(x => x.Id == attributed.Id);
        Assert.Equal(creator, persisted.CreatedByUserId);
        Assert.Equal(editor, persisted.UpdatedByUserId);
        Assert.Null(persisted.DeactivatedByUserId);
        Assert.Equal(createdAt, persisted.CreatedAtUtc);
        var old = await context.Customers.SingleAsync(x => x.Id == legacy.Id);
        Assert.Null(old.CreatedByUserId);
        Assert.Null(old.UpdatedByUserId);
        Assert.Null(old.DeactivatedByUserId);
    }

    [Fact]
    public async Task SaveWithoutChanges_ShouldNotAttributeLegacyRecordsOrChangeTimestamps()
    {
        Guid id;
        DateTime createdAt;
        await using (var seed = Context())
        {
            var customer = new Customer { Code = "UNCHANGED", Name = "Legacy" };
            seed.Customers.Add(customer);
            await seed.SaveChangesAsync();
            id = customer.Id;
            createdAt = customer.CreatedAtUtc;
        }
        await using var context = Context(new Actor(Guid.NewGuid()));
        await context.Customers.SingleAsync();
        Assert.Equal(0, await context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        var saved = await context.Customers.SingleAsync(x => x.Id == id);
        Assert.Equal(createdAt, saved.CreatedAtUtc);
        Assert.Null(saved.CreatedByUserId);
        Assert.Null(saved.UpdatedByUserId);
        Assert.Null(saved.UpdatedAtUtc);
    }

    [Fact]
    public void ProductionAuditRegistration_ShouldBeScopedAndAllowNonHttpToolsWithoutCurrentUser()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:InventoryDatabase"] = "Server=localhost;Database=AuditRegistrationTest;Integrated Security=true;"
        }).Build();
        services.AddLogging();
        services.AddDataAccess(configuration);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(AuditSaveChangesInterceptor)).Lifetime);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var interceptor = first.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>();
        Assert.Same(interceptor, first.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
        Assert.NotSame(interceptor, second.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
    }

    public void Dispose() => _connection.Dispose();
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Business.Common.Security;
using DataAccess.Identity;
using DataAccess.Persistence;
using Entities.Entities;
using InventoryManagement.AccountProvisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebAPI.Contracts.Auth;

namespace Tests.Integration;

public class AccountProvisioningTests : IDisposable
{
    private readonly AccountTestFactory factory = new();
    private readonly string email = "account-" + Guid.NewGuid().ToString("N") + "@example.test";
    private readonly string password = "aA!9" + Guid.NewGuid().ToString("N");

    private async Task<(Guid AdminId, Guid CustomerId, string AdminSnapshot)> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(ApplicationRoles.Admin) { Id = Guid.NewGuid() })).Succeeded);
        var admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = "admin@example.test", Email = "admin@example.test" };
        Assert.True((await users.CreateAsync(admin, "aA!9" + Guid.NewGuid().ToString("N"))).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, ApplicationRoles.Admin)).Succeeded);
        var customer = new Customer { Code = "EXISTING", Name = "Existing customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return (admin.Id, customer.Id, JsonSerializer.Serialize(admin));
    }

    private async Task<AccountProvisioningResult> CreateAsync(string role, Guid? customerId = null,
        string? newEmail = null, string? newPassword = null, bool confirmed = true)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AccountProvisioner>()
            .ProvisionAsync(newEmail ?? email, newPassword ?? password, role, customerId, confirmed);
    }

    [Theory]
    [InlineData(ApplicationRoles.WarehouseStaff)]
    [InlineData(ApplicationRoles.Customer)]
    public async Task CreateAccount_PreservesAdminAndUsesIdentityHashAndOnlyRequestedRole(string role)
    {
        var seed = await SeedAsync();
        var result = await CreateAsync(role, role == ApplicationRoles.Customer ? seed.CustomerId : null);
        Assert.Equal(AccountProvisioningStatus.Created, result.Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var saved = (await users.FindByIdAsync(result.UserId!.Value.ToString()))!;
        Assert.True(!string.IsNullOrEmpty(saved.PasswordHash) && saved.PasswordHash != password);
        Assert.True(await users.CheckPasswordAsync(saved, password));
        Assert.Equal(new[] { role }, await users.GetRolesAsync(saved));
        Assert.Equal(role == ApplicationRoles.Customer ? seed.CustomerId : (Guid?)null, saved.CustomerId);
        var admin = (await users.FindByIdAsync(seed.AdminId.ToString()))!;
        Assert.True(JsonSerializer.Serialize(admin) == seed.AdminSnapshot, "Existing Admin must remain byte-for-byte unchanged.");
        Assert.Equal(new[] { ApplicationRoles.Admin }, await users.GetRolesAsync(admin));
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(2, await db.Roles.CountAsync());
        Assert.Single(await db.Customers.ToListAsync());
        Assert.Empty(await db.Products.ToListAsync());
        Assert.DoesNotContain(factory.Log.Messages, message => message.Contains(password, StringComparison.Ordinal)
            || message.Contains(email, StringComparison.Ordinal) || message.Contains(saved.PasswordHash!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedEmail_IsCaseInsensitiveAndDoesNotResetPasswordOrChangeRole()
    {
        await SeedAsync();
        var first = await CreateAsync(ApplicationRoles.WarehouseStaff);
        var repeated = await CreateAsync(ApplicationRoles.Customer, Guid.NewGuid(), email.ToUpperInvariant(), "aA!9" + Guid.NewGuid().ToString("N"));
        Assert.Equal(AccountProvisioningStatus.Created, first.Status);
        Assert.Equal(AccountProvisioningStatus.ExistingAccount, repeated.Status);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        Assert.True(await users.CheckPasswordAsync(user, password));
        Assert.Equal(new[] { ApplicationRoles.WarehouseStaff }, await users.GetRolesAsync(user));
        Assert.Equal(2, await users.Users.CountAsync());
    }

    [Fact]
    public async Task ExistingAdminEmail_CannotBeChangedOrDemoted()
    {
        var seed = await SeedAsync();
        Assert.Equal(AccountProvisioningStatus.ExistingAccount,
            (await CreateAsync(ApplicationRoles.WarehouseStaff, newEmail: "ADMIN@example.test")).Status);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(seed.AdminId.ToString());
        Assert.True(JsonSerializer.Serialize(user) == seed.AdminSnapshot);
    }

    [Fact]
    public async Task CustomerLink_RejectsSecondAccountWithoutUnlinkingFirst()
    {
        var seed = await SeedAsync();
        var first = await CreateAsync(ApplicationRoles.Customer, seed.CustomerId);
        Assert.Equal(AccountProvisioningStatus.Created, first.Status);
        var second = await CreateAsync(ApplicationRoles.Customer, seed.CustomerId, "other@example.test");
        Assert.Equal(AccountProvisioningStatus.CustomerUnavailable, second.Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(first.UserId, (await db.Users.SingleAsync(user => user.CustomerId == seed.CustomerId)).Id);
        Assert.Equal(2, await db.Users.CountAsync());
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing")]
    public async Task CustomerMustExistAndBeActive(string scenario)
    {
        var seed = await SeedAsync();
        var id = seed.CustomerId;
        if (scenario == "inactive")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            (await db.Customers.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }
        else id = Guid.NewGuid();
        Assert.Equal(AccountProvisioningStatus.CustomerUnavailable, (await CreateAsync(ApplicationRoles.Customer, id)).Status);
        await AssertOnlyAdminAsync();
    }

    [Theory]
    [InlineData(ApplicationRoles.Admin)]
    [InlineData(ApplicationRoles.Viewer)]
    [InlineData("customer")]
    [InlineData("Unknown")]
    [InlineData("")]
    public async Task ArbitraryRolesAndAdmin_AreRejected(string role)
    {
        await SeedAsync();
        Assert.Equal(AccountProvisioningStatus.InvalidInput, (await CreateAsync(role)).Status);
        await AssertOnlyAdminAsync();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("policy")]
    [InlineData("long")]
    [InlineData("email")]
    [InlineData("staffLink")]
    [InlineData("customerNoLink")]
    [InlineData("customerEmptyLink")]
    public async Task InvalidInput_DoesNotWriteAccountsOrRoles(string scenario)
    {
        var seed = await SeedAsync();
        var role = scenario.StartsWith("customer") ? ApplicationRoles.Customer : ApplicationRoles.WarehouseStaff;
        var inputPassword = scenario switch { "short" => "aA!9" + new string('x', 15),
            "policy" => new string('a', 24), "long" => "aA!9" + new string('x', 1021), _ => password };
        Guid? customerId = scenario == "staffLink" ? seed.CustomerId : scenario == "customerEmptyLink" ? Guid.Empty : null;
        var result = await CreateAsync(role, customerId, scenario == "email" ? "invalid-email" : email, inputPassword);
        Assert.Equal(AccountProvisioningStatus.InvalidInput, result.Status);
        await AssertOnlyAdminAsync();
    }

    [Fact]
    public async Task MissingConfirmation_DoesNotWriteAnything()
    {
        await SeedAsync();
        Assert.Equal(AccountProvisioningStatus.NotConfirmed, (await CreateAsync(ApplicationRoles.WarehouseStaff, confirmed: false)).Status);
        await AssertOnlyAdminAsync();
    }

    [Fact]
    public async Task ProductionEnvironment_RejectsEvenExplicitConfirmedCreation()
    {
        await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var provisioner = new AccountProvisioner(services.GetRequiredService<InventoryDbContext>(),
            services.GetRequiredService<UserManager<ApplicationUser>>(), services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            services.GetRequiredService<IAccountProvisioningTarget>(), new TestEnvironment());
        Assert.Equal(AccountProvisioningStatus.NotDevelopment,
            (await provisioner.ProvisionAsync(email, password, ApplicationRoles.WarehouseStaff, null, true)).Status);
        await AssertOnlyAdminAsync();
    }

    [Fact]
    public async Task FailedTargetPreflight_RejectsCreation()
    {
        await SeedAsync();
        factory.TargetAllowed = false;
        Assert.Equal(AccountProvisioningStatus.UnsafeTarget, (await CreateAsync(ApplicationRoles.WarehouseStaff)).Status);
        await AssertOnlyAdminAsync();
    }

    [Fact]
    public async Task FailedRoleAssignment_RollsBackNewUserRoleAndCustomerLink()
    {
        var seed = await SeedAsync();
        factory.RejectAssignment = true;
        Assert.Equal(AccountProvisioningStatus.Failed, (await CreateAsync(ApplicationRoles.Customer, seed.CustomerId)).Status);
        await AssertOnlyAdminAsync();
        factory.RejectAssignment = false;
        Assert.Equal(AccountProvisioningStatus.Created, (await CreateAsync(ApplicationRoles.Customer, seed.CustomerId)).Status);
    }

    [Theory]
    [InlineData(ApplicationRoles.WarehouseStaff)]
    [InlineData(ApplicationRoles.Customer)]
    public async Task ProvisionedAccount_UsesExistingCookieLoginAndServerPolicies(string role)
    {
        var seed = await SeedAsync();
        Assert.Equal(AccountProvisioningStatus.Created,
            (await CreateAsync(role, role == ApplicationRoles.Customer ? seed.CustomerId : null)).Status);
        using var client = factory.CreateClient();
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf"))!;
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            { Content = JsonContent.Create(new { email, password }) };
        login.Headers.Add(csrf.HeaderName, csrf.RequestToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        var me = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.Equal(new[] { role }, me.Roles);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(role == ApplicationRoles.Customer ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
            (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(role == ApplicationRoles.Customer ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
            (await client.GetAsync("/api/stock")).StatusCode);
        var portal = await client.GetAsync("/api/customer-portal/me");
        Assert.Equal(role == ApplicationRoles.Customer ? HttpStatusCode.OK : HttpStatusCode.Forbidden, portal.StatusCode);
        if (role == ApplicationRoles.Customer)
        {
            using var json = JsonDocument.Parse(await portal.Content.ReadAsStringAsync());
            Assert.Equal("Existing customer", json.RootElement.GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task UnexpectedException_RollsBackNewRoleUserAndMembership()
    {
        await SeedAsync();
        factory.ThrowDuringAssignment = true;
        Assert.Equal(AccountProvisioningStatus.Failed, (await CreateAsync(ApplicationRoles.WarehouseStaff)).Status);
        await AssertOnlyAdminAsync();
    }

    [Fact]
    public async Task RealTargetGuard_RejectsSqliteEvenInDevelopment()
    {
        await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var guard = new AccountProvisioningTarget(scope.ServiceProvider.GetRequiredService<InventoryDbContext>(),
            scope.ServiceProvider.GetRequiredService<IHostEnvironment>());
        Assert.False(await guard.ValidateAsync());
        await AssertOnlyAdminAsync();
    }

    [Fact]
    public void WebApi_DoesNotRegisterLocalAccountProvisioner()
    {
        using var webFactory = new CustomWebApplicationFactory();
        using var scope = webFactory.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<AccountProvisioner>());
        Assert.Null(scope.ServiceProvider.GetService<IAccountProvisioningTarget>());
    }

    private async Task AssertOnlyAdminAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Roles.CountAsync());
        Assert.Equal(1, await db.Set<IdentityUserRole<Guid>>().CountAsync());
        Assert.False(await db.Users.AnyAsync(user => user.CustomerId != null));
    }

    public void Dispose() => factory.Dispose();

    private sealed class AccountTestFactory : CustomWebApplicationFactory
    {
        public bool TargetAllowed { get; set; } = true;
        public bool RejectAssignment { get; set; }
        public bool ThrowDuringAssignment { get; set; }
        public RecordingLogProvider Log { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(this);
                services.AddScoped<IAccountProvisioningTarget, TestTarget>();
                services.AddScoped<AccountProvisioner>();
                services.AddScoped<UserManager<ApplicationUser>, RejectingUserManager>();
                services.AddLogging(logging => logging.AddProvider(Log));
            });
        }
    }

    private sealed class TestTarget(AccountTestFactory factory) : IAccountProvisioningTarget
    {
        public Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(factory.TargetAllowed);
    }

    private sealed class RejectingUserManager(
        IUserStore<ApplicationUser> store, IOptions<IdentityOptions> options,
        IPasswordHasher<ApplicationUser> hasher, IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators, ILookupNormalizer normalizer,
        IdentityErrorDescriber describer, IServiceProvider services, ILogger<UserManager<ApplicationUser>> logger,
        AccountTestFactory factory)
        : UserManager<ApplicationUser>(store, options, hasher, userValidators, passwordValidators,
            normalizer, describer, services, logger)
    {
        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
            => factory.ThrowDuringAssignment && role != ApplicationRoles.Admin
                ? throw new InvalidOperationException("Injected transaction failure")
                : factory.RejectAssignment && role != ApplicationRoles.Admin
                ? Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "InjectedFailure" }))
                : base.AddToRoleAsync(user, role);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "AccountTests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLogProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);
        public void Dispose() { }
        private sealed class RecordingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}

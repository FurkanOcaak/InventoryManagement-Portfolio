using System.Collections.Concurrent;
using DataAccess.Identity;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebAPI.Infrastructure.Provisioning;

namespace Tests.Integration;

public class FirstAdminProvisioningTests : IDisposable
{
    private readonly ProvisioningTestFactory _factory = new();
    private readonly string _email = "bootstrap-" + Guid.NewGuid().ToString("N") + "@example.test";
    private readonly string _password = "aA!9" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task EmptyDatabase_ShouldCreateHashedAdminAndAllowIdentitySignIn()
    {
        var result = await ProvisionAsync();
        Assert.Equal(ProvisioningStatus.Created, result.Status);
        Assert.NotNull(result.UserId);
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var saved = await users.FindByIdAsync(result.UserId.Value.ToString());
        Assert.NotNull(saved);
        Assert.True(!string.IsNullOrEmpty(saved.PasswordHash) && saved.PasswordHash != _password);
        Assert.True(await users.CheckPasswordAsync(saved, _password));
        Assert.True(await users.IsInRoleAsync(saved, FirstAdminProvisioner.AdminRole));
        Assert.True((await signIn.CheckPasswordSignInAsync(saved, _password, false)).Succeeded);
        Assert.False(await users.IsLockedOutAsync(saved));
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(1, await context.Roles.CountAsync());
        Assert.Equal(1, await context.Set<IdentityUserRole<Guid>>().CountAsync());
    }

    [Fact]
    public async Task CreatedAdmin_ShouldRejectWrongPassword()
    {
        Assert.Equal(ProvisioningStatus.Created, (await ProvisionAsync()).Status);
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(_email);
        Assert.NotNull(user);
        Assert.False((await signIn.CheckPasswordSignInAsync(user,
            "aA!9" + Guid.NewGuid().ToString("N"), false)).Succeeded);
    }

    [Fact]
    public async Task ExistingNonAdmin_ShouldNotBePromotedOrHavePasswordChanged()
    {
        string? originalHash;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var existing = new ApplicationUser { Id = Guid.NewGuid(), UserName = _email, Email = _email };
            Assert.True((await users.CreateAsync(existing, _password)).Succeeded);
            originalHash = existing.PasswordHash;
        }
        var result = await ProvisionAsync();
        Assert.Equal(ProvisioningStatus.ExistingUsers, result.Status);
        using var check = _factory.Services.CreateScope();
        var manager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = await manager.FindByEmailAsync(_email);
        Assert.NotNull(persisted);
        Assert.True(persisted.PasswordHash == originalHash);
        Assert.False(await manager.IsInRoleAsync(persisted, FirstAdminProvisioner.AdminRole));
        var context = check.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Empty(await context.Roles.ToListAsync());
    }

    [Fact]
    public async Task RepeatedInvocation_ShouldNotCreateSecondAdminOrResetPassword()
    {
        var first = await ProvisionAsync();
        Assert.Equal(ProvisioningStatus.Created, first.Status);
        var replacement = "aA!9" + Guid.NewGuid().ToString("N");
        using (var scope = _factory.Services.CreateScope())
        {
            var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();
            var again = await provisioner.ProvisionAsync(_email, replacement, true);
            Assert.Equal(ProvisioningStatus.ExistingUsers, again.Status);
        }
        using var check = _factory.Services.CreateScope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var saved = await users.FindByIdAsync(first.UserId!.Value.ToString());
        Assert.NotNull(saved);
        Assert.True(await users.CheckPasswordAsync(saved, _password));
        Assert.False(await users.CheckPasswordAsync(saved, replacement));
        var context = check.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(1, await context.Roles.CountAsync());
    }

    [Theory]
    [InlineData("email")]
    [InlineData("short")]
    [InlineData("policy")]
    public async Task InvalidInput_ShouldNotWriteUsersOrRoles(string invalid)
    {
        using var scope = _factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();
        var email = invalid == "email" ? "invalid-email" : _email;
        var password = invalid switch
        {
            "short" => Guid.NewGuid().ToString("N")[..19],
            "policy" => Guid.NewGuid().ToString("N"),
            _ => _password
        };
        var result = await provisioner.ProvisionAsync(email, password, true);
        Assert.Equal(ProvisioningStatus.InvalidInput, result.Status);
        await AssertEmptyIdentityAsync(_factory);
    }

    [Fact]
    public async Task MissingConfirmation_ShouldNotWriteAnything()
    {
        using var scope = _factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();
        var result = await provisioner.ProvisionAsync(_email, _password, confirmed: false);
        Assert.Equal(ProvisioningStatus.NotConfirmed, result.Status);
        await AssertEmptyIdentityAsync(_factory);
    }

    [Fact]
    public async Task ProductionEnvironment_ShouldRejectProvisioning()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var provisioner = new FirstAdminProvisioner(
            services.GetRequiredService<InventoryDbContext>(),
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            services.GetRequiredService<SignInManager<ApplicationUser>>(),
            new TestEnvironment { EnvironmentName = Environments.Production });
        var result = await provisioner.ProvisionAsync(_email, _password, true);
        Assert.Equal(ProvisioningStatus.NotDevelopment, result.Status);
        await AssertEmptyIdentityAsync(_factory);
    }

    [Fact]
    public async Task RoleAssignmentFailure_ShouldRollbackUserRoleAndMembership()
    {
        using var factory = new ProvisioningTestFactory(rejectRoleAssignment: true);
        using (var scope = factory.Services.CreateScope())
        {
            var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();
            var result = await provisioner.ProvisionAsync(_email, _password, true);
            Assert.Equal(ProvisioningStatus.Failed, result.Status);
        }
        await AssertEmptyIdentityAsync(factory);
    }

    [Fact]
    public async Task ExistingAdminRoleWithoutUsers_ShouldBeReused()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(FirstAdminProvisioner.AdminRole)
            {
                Id = Guid.NewGuid()
            })).Succeeded);
        }
        Assert.Equal(ProvisioningStatus.Created, (await ProvisionAsync()).Status);
        using var check = _factory.Services.CreateScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<InventoryDbContext>().Roles.CountAsync());
    }

    [Fact]
    public async Task Provisioning_ShouldNotLogCredentialsOrPasswordHash()
    {
        Assert.Equal(ProvisioningStatus.Created, (await ProvisionAsync()).Status);
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync(_email);
        Assert.NotNull(user);
        var credentialsLogged = _factory.Log.Messages.Any(message => message.Contains(_password, StringComparison.Ordinal)
            || message.Contains(_email, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(user.PasswordHash) && message.Contains(user.PasswordHash, StringComparison.Ordinal)));
        Assert.False(credentialsLogged, "Credentials must not be logged.");
    }

    private async Task<ProvisioningResult> ProvisionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>()
            .ProvisionAsync(_email, _password, true);
    }

    private static async Task AssertEmptyIdentityAsync(CustomWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Empty(await context.Users.ToListAsync());
        Assert.Empty(await context.Roles.ToListAsync());
        Assert.Empty(await context.Set<IdentityUserRole<Guid>>().ToListAsync());
    }

    public void Dispose() => _factory.Dispose();

    private sealed class ProvisioningTestFactory(bool rejectRoleAssignment = false) : CustomWebApplicationFactory
    {
        public RecordingLogProvider Log { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddScoped<FirstAdminProvisioner>();
                services.AddLogging(logging => logging.AddProvider(Log));
                if (rejectRoleAssignment)
                    services.AddScoped<UserManager<ApplicationUser>, RejectRoleAssignmentUserManager>();
            });
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ProvisioningTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RejectRoleAssignmentUserManager(
        IUserStore<ApplicationUser> store, IOptions<IdentityOptions> options,
        IPasswordHasher<ApplicationUser> hasher, IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators, ILookupNormalizer normalizer,
        IdentityErrorDescriber describer, IServiceProvider services, ILogger<UserManager<ApplicationUser>> logger)
        : UserManager<ApplicationUser>(store, options, hasher, userValidators, passwordValidators,
            normalizer, describer, services, logger)
    {
        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
            => Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "SimulatedRoleAssignmentFailure" }));
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

using InventoryManagement.AdminProvisioning;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using WebAPI.Infrastructure.Provisioning;

namespace Tests.Security;

public class ProvisioningCommandTests
{
    [Fact]
    public void ConsoleHost_ShouldResolveProvisioningWithoutStartingHttpServer()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:InventoryDatabase"] =
                "Server=(localdb)\\MSSQLLocalDB;Database=Unused_Test;Trusted_Connection=True;"
        });
        builder.Services.AddProvisioningServices(builder.Configuration, builder.Environment);
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ProvisioningTargetGuard>());
        Assert.Null(scope.ServiceProvider.GetService<IServer>());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("--create-first-admin", true)]
    [InlineData("--create-first-admin --yes", false)]
    [InlineData("--create-first-admin --password ignored", false)]
    [InlineData("--help", false)]
    [InlineData("--check-target", false)]
    [InlineData("--CREATE-FIRST-ADMIN", false)]
    public void Command_ShouldRequireExactExplicitInvocation(string arguments, bool expected)
    {
        Assert.Equal(expected, ProvisioningCommand.IsExplicitInvocation(
            arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    }

    [Theory]
    [InlineData("localhost,14331", "InventoryManagement_Dev", true)]
    [InlineData("127.0.0.1,14331", "InventoryManagement_Dev", true)]
    [InlineData("tcp:localhost,14331", "InventoryManagement_Dev", true)]
    [InlineData("localhost,14331", "OtherDatabase", false)]
    [InlineData("localhost,1433", "InventoryManagement_Dev", false)]
    [InlineData("remote.example.test,14331", "InventoryManagement_Dev", false)]
    [InlineData("localhost", "InventoryManagement_Dev", false)]
    public void TargetGuard_ShouldRejectUnexpectedDatabaseOrInstance(string server, string database, bool expected)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = database, IntegratedSecurity = true
        };
        Assert.Equal(expected, ProvisioningTargetGuard.IsExpectedConnection(builder.ConnectionString));
    }
}

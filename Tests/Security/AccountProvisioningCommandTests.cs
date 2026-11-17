using System.Text.Json.Nodes;
using Business.Common.Security;
using InventoryManagement.AccountProvisioning;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebAPI.Infrastructure.Provisioning;

namespace Tests.Security;

public class AccountProvisioningCommandTests
{
    [Theory]
    [InlineData("--create-warehouse-staff", ApplicationRoles.WarehouseStaff)]
    [InlineData("--create-customer", ApplicationRoles.Customer)]
    [InlineData("--create-first-admin", null)]
    [InlineData("--create-admin", null)]
    [InlineData("--create-customer --password=secret", null)]
    [InlineData("--create-warehouse-staff user@example.test", null)]
    [InlineData("", null)]
    [InlineData("--check-target", null)]
    public void Command_OnlyAcceptsExplicitAllowedRoleWithNoCredentials(string args, string? expected)
        => Assert.Equal(expected, AccountProvisioningCommand.ParseRole(
            args.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void ConsoleHost_ResolvesOnlyAccountProvisionerWithoutFirstAdminOrHttpServer()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:InventoryDatabase"] = "Server=(localdb)\\MSSQLLocalDB;Database=Unused;Trusted_Connection=True;" });
        builder.Services.AddAccountProvisioningServices(builder.Configuration, builder.Environment);
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AccountProvisioner>());
        Assert.IsType<AccountProvisioningTarget>(scope.ServiceProvider.GetRequiredService<IAccountProvisioningTarget>());
        Assert.Null(scope.ServiceProvider.GetService<FirstAdminProvisioner>());
        Assert.Null(scope.ServiceProvider.GetService<IServer>());
    }

    [Theory]
    [InlineData("localhost,14331", "InventoryManagement_Dev", true)]
    [InlineData("127.0.0.1,14331", "InventoryManagement_Dev", true)]
    [InlineData("tcp:localhost,14331", "InventoryManagement_Dev", true)]
    [InlineData("tcp:127.0.0.1,14331", "InventoryManagement_Dev", true)]
    [InlineData("localhost,14331", "Other", false)]
    [InlineData("remote.example.test,14331", "InventoryManagement_Dev", false)]
    [InlineData("localhost,1433", "InventoryManagement_Dev", false)]
    public void TargetGuard_FailsClosedForOtherConnections(string server, string database, bool expected)
    {
        var connection = new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = database, IntegratedSecurity = true };
        Assert.Equal(expected, AccountProvisioningTarget.IsExpectedConnection(connection.ConnectionString));
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("name", false)]
    [InlineData("running", false)]
    [InlineData("health", false)]
    [InlineData("project", false)]
    [InlineData("volume", false)]
    [InlineData("destination", false)]
    [InlineData("type", false)]
    [InlineData("port", false)]
    [InlineData("host", false)]
    [InlineData("malformed", false)]
    public void DockerGuard_RequiresExactContainerProjectVolumeHealthAndLocalPort(string variant, bool expected)
    {
        var json = JsonNode.Parse("""
            [{"Name":"/inventory_sqlserver","State":{"Running":true,"Health":{"Status":"healthy"}},
            "Config":{"Labels":{"com.docker.compose.project":"inventorymanagement"}},
            "Mounts":[{"Type":"volume","Name":"inventorymanagement_inventory_sql_data","Destination":"/var/opt/mssql"}],
            "NetworkSettings":{"Ports":{"1433/tcp":[{"HostIp":"127.0.0.1","HostPort":"14331"}]}}}]
            """)!;
        var node = json[0]!;
        switch (variant)
        {
            case "name": node["Name"] = "/other"; break;
            case "running": node["State"]!["Running"] = false; break;
            case "health": node["State"]!["Health"]!["Status"] = "unhealthy"; break;
            case "project": node["Config"]!["Labels"]!["com.docker.compose.project"] = "other"; break;
            case "volume": node["Mounts"]![0]!["Name"] = "other"; break;
            case "destination": node["Mounts"]![0]!["Destination"] = "/other"; break;
            case "type": node["Mounts"]![0]!["Type"] = "bind"; break;
            case "port": node["NetworkSettings"]!["Ports"]!["1433/tcp"]![0]!["HostPort"] = "1433"; break;
            case "host": node["NetworkSettings"]!["Ports"]!["1433/tcp"]![0]!["HostIp"] = "0.0.0.0"; break;
        }
        Assert.Equal(expected, AccountProvisioningTarget.IsExpectedDockerInspection(variant == "malformed" ? "{}" : json.ToJsonString()));
    }

    [Fact]
    public void CustomerLabels_RemoveControlCharactersAndLimitOutput()
    {
        var value = AccountProvisioningCommand.SafeLabel("Customer\r\n\t\u001b" + new string('x', 100));
        Assert.Equal(80, value.Length);
        Assert.DoesNotContain(value, char.IsControl);
    }
}

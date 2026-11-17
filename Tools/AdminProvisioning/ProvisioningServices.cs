using DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebAPI.Infrastructure.Provisioning;
using WebAPI.Infrastructure.Security;

namespace InventoryManagement.AdminProvisioning;

public static class ProvisioningServices
{
    public static IServiceCollection AddProvisioningServices(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<Business.Common.Security.ICurrentUser, HttpContextCurrentUser>();
        // Shared authentication configuration includes endpoint-aware rate limiter services.
        // Routing services are needed for DI validation; no HTTP server is started.
        services.AddRouting();
        services.AddDataAccess(configuration);
        services.AddInventoryAuthentication(environment);
        services.AddScoped<FirstAdminProvisioner>();
        services.AddScoped<ProvisioningTargetGuard>();
        return services;
    }
}

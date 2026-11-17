using Business.Common.Security;
using DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebAPI.Infrastructure.Security;

namespace InventoryManagement.AccountProvisioning;

public static class AccountProvisioningServices
{
    public static IServiceCollection AddAccountProvisioningServices(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddRouting();
        services.AddDataAccess(configuration);
        services.AddInventoryAuthentication(environment);
        services.AddScoped<IAccountProvisioningTarget, AccountProvisioningTarget>();
        services.AddScoped<AccountProvisioner>();
        return services;
    }
}

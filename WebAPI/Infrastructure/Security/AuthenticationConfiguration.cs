using System.Globalization;
using System.Threading.RateLimiting;
using DataAccess.Identity;
using Business.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace WebAPI.Infrastructure.Security;

public static class AuthenticationConfiguration
{
    public const string LoginRateLimit = "auth-login";
    public const string CsrfHeader = "X-CSRF-TOKEN";
    public const string AuthenticationCookie = "InventoryManagement.Auth";

    public static IServiceCollection AddInventoryAuthentication(
        this IServiceCollection services, IHostEnvironment environment)
    {
        var securePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        new IdentityBuilder(typeof(ApplicationUser), typeof(IdentityRole<Guid>), services)
            .AddSignInManager();

        services.Configure<IdentityOptions>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        });

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        }).AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthenticationCookie;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = securePolicy;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        services.AddMemoryCache();
        services.AddSingleton<ITicketStore, MemoryAuthenticationTicketStore>();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<ITicketStore>((options, store) => options.SessionStore = store);
        services.AddSingleton<UnknownUserPasswordVerifier>();
        services.AddScoped<IAuthorizationHandler, LinkedCustomerHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ApplicationPolicies.CustomerShopping, builder => builder
                .RequireAuthenticatedUser().RequireRole(ApplicationRoles.Customer)
                .AddRequirements(new LinkedCustomerRequirement()));
            options.AddPolicy(ApplicationPolicies.OrdersRead, builder => builder
                .RequireAuthenticatedUser().RequireRole(ApplicationRoles.Admin));
            options.AddPolicy(ApplicationPolicies.CustomerPortal, builder => builder
                .RequireAuthenticatedUser().RequireRole(ApplicationRoles.Customer)
                .AddRequirements(new LinkedCustomerRequirement()));
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var policy in new[] { ApplicationPolicies.ProductsRead,
                ApplicationPolicies.WarehousesRead, ApplicationPolicies.StockRead })
                options.AddPolicy(policy, builder => builder.RequireAuthenticatedUser().RequireRole(
                    ApplicationRoles.Admin, ApplicationRoles.WarehouseStaff, ApplicationRoles.Viewer));
            foreach (var policy in new[] { ApplicationPolicies.ProductsManage, ApplicationPolicies.WarehousesManage,
                ApplicationPolicies.CustomersRead, ApplicationPolicies.CustomersManage })
                options.AddPolicy(policy, builder => builder.RequireAuthenticatedUser().RequireRole(ApplicationRoles.Admin));
            foreach (var policy in new[] { ApplicationPolicies.StockIn, ApplicationPolicies.StockOut })
                options.AddPolicy(policy, builder => builder.RequireAuthenticatedUser().RequireRole(
                    ApplicationRoles.Admin, ApplicationRoles.WarehouseStaff));
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeader;
            options.Cookie.Name = "InventoryManagement.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = securePolicy;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            options.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(CultureInfo.InvariantCulture);
                await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Çok fazla giriş denemesi. Lütfen daha sonra tekrar deneyin.")
                    .ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}

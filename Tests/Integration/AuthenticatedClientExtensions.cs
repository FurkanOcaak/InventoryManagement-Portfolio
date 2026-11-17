using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Business.Common.Security;
using DataAccess.Identity;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Contracts.Auth;

namespace Tests.Integration;

public static class AuthenticatedClientExtensions
{
    private sealed record Session(string Cookies, CsrfResponse Csrf);
    private static readonly ConditionalWeakTable<CustomWebApplicationFactory,
        ConcurrentDictionary<string, Lazy<Task<Session>>>> Sessions = new();

    // Explicit per-test role selection; normal CreateClient() remains anonymous.
    // Reuse a real session to avoid CRUD fixtures exhausting the login limiter.
    public static async Task<HttpClient> CreateRoleClientAsync(this CustomWebApplicationFactory factory,
        string role, bool withCsrf = true, bool freshSession = false)
    {
        if (role is not (ApplicationRoles.Admin or ApplicationRoles.WarehouseStaff or ApplicationRoles.Viewer or ApplicationRoles.Customer))
            throw new ArgumentException("Select an application role explicitly.", nameof(role));
        var sessions = Sessions.GetValue(factory, _ => new());
        var session = freshSession ? await SignInAsync(factory, role)
            : await sessions.GetOrAdd(role, _ => new Lazy<Task<Session>>(
                () => SignInAsync(factory, role))).Value;
        var client = NewClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", session.Cookies);
        if (withCsrf) client.DefaultRequestHeaders.Add(session.Csrf.HeaderName, session.Csrf.RequestToken);
        return client;
    }

    private static HttpClient NewClient(CustomWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("http://localhost")
        });

    private static async Task<Session> SignInAsync(CustomWebApplicationFactory factory, string role)
    {
        var password = "aA!9" + Guid.NewGuid().ToString("N");
        var email = "permission-" + Guid.NewGuid().ToString("N") + "@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(role))
                Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() })).Succeeded);
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        using var client = NewClient(factory);
        var cookies = new CookieContainer();
        var origin = client.BaseAddress!;
        async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            using (request)
            {
                var cookie = cookies.GetCookieHeader(origin);
                if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
                var response = await client.SendAsync(request);
                if (response.Headers.TryGetValues("Set-Cookie", out var headers))
                    foreach (var header in headers) cookies.SetCookies(origin, header);
                return response;
            }
        }
        using var first = await SendAsync(new(HttpMethod.Get, "/api/auth/csrf"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var csrf = await first.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrf);
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = email, Password = password })
        };
        loginRequest.Headers.Add(csrf.HeaderName, csrf.RequestToken);
        using var login = await SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var second = await SendAsync(new(HttpMethod.Get, "/api/auth/csrf"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var authenticatedToken = await second.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(authenticatedToken);
        return new Session(cookies.GetCookieHeader(origin), authenticatedToken);
    }
}

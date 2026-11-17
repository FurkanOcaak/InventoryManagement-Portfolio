using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Business.Common.Security;
using DataAccess.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using WebAPI.Contracts.Auth;
using WebAPI.Infrastructure.Security;

namespace Tests.Integration;

public class AuthApiTests : IDisposable
{
    private readonly AuthTestFactory _factory = new();
    private readonly string _password = "aA!9" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Csrf_ShouldIssuePairedTokenWithoutCaching()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.RequestToken));
        Assert.Equal(AuthenticationConfiguration.CsrfHeader, token.HeaderName);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var cookie = FindCookie(response, "InventoryManagement.Csrf");
        Assert.NotNull(cookie);
        Assert.True(cookie.HttpOnly);
    }

    [Fact]
    public async Task Login_ShouldIssueHttpOnlyCookieAndMeShouldReturnCurrentUserGuidAndRoles()
    {
        var user = await CreateUserAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Viewer"))).Succeeded);
            var trackedUser = await users.FindByIdAsync(user.Id.ToString());
            Assert.NotNull(trackedUser);
            Assert.True((await users.AddToRoleAsync(trackedUser, "Viewer")).Succeeded);
        }
        using var client = CreateClient();
        using var response = await LoginAsync(client, user.Email!, _password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = FindCookie(response, AuthenticationConfiguration.AuthenticationCookie);
        Assert.NotNull(cookie);
        Assert.True(cookie.HttpOnly);
        Assert.False(cookie.Secure);
        Assert.Equal("Lax", cookie.SameSite.ToString());
        Assert.Equal("/", cookie.Path.ToString());
        using var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(me);
        Assert.Equal(user.Id, me.Id);
        Assert.Equal(user.Email, me.Email);
        Assert.Contains("Viewer", me.Roles);
        Assert.True(meResponse.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Me_WhenAnonymous_ShouldReturn401WithoutHtmlRedirect()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownEmail_ShouldReturnSameGenericFailureWithoutAuthCookie()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient();
        using var wrong = await LoginAsync(client, user.Email!, "aA!9" + Guid.NewGuid().ToString("N"));
        using var unknown = await LoginAsync(client, "missing-" + Guid.NewGuid().ToString("N") + "@example.test", _password);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Null(FindCookie(wrong, AuthenticationConfiguration.AuthenticationCookie));
        Assert.Null(FindCookie(unknown, AuthenticationConfiguration.AuthenticationCookie));
        var first = await wrong.Content.ReadFromJsonAsync<ProblemDetails>();
        var second = await unknown.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Detail, second.Detail);
        var body = await wrong.Content.ReadAsStringAsync();
        Assert.False(body.Contains(user.Email!, StringComparison.Ordinal));
        Assert.False(body.Contains(_password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logout_ShouldInvalidateSessionAndRemainSafeWhenRepeated()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient();
        using var login = await LoginAsync(client, user.Email!, _password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var csrf = await GetCsrfAsync(client);
        using var logout = await SendAsync(client, "/api/auth/logout", csrf);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        var anonymousCsrf = await GetCsrfAsync(client);
        using var repeated = await SendAsync(client, "/api/auth/logout", anonymousCsrf);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
    }

    [Fact]
    public async Task LoggedOutCookieCopy_ShouldNotRestoreSession()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient();
        using var login = await LoginAsync(client, user.Email!, _password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = FindCookie(login, AuthenticationConfiguration.AuthenticationCookie);
        Assert.NotNull(cookie);
        var savedCookie = cookie.Name + "=" + cookie.Value;
        using var logout = await SendAsync(client, "/api/auth/logout", await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var replay = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.TryAddWithoutValidation("Cookie", savedCookie);
        using var response = await replay.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_WhenCsrfIsMissingOrInvalid_ShouldNotSignInOrIncrementFailures(bool invalid)
    {
        var user = await CreateUserAsync();
        using var client = CreateClient();
        await GetCsrfAsync(client);
        using var response = await SendAsync(client, "/api/auth/login",
            invalid ? new CsrfResponse("invalid-token", AuthenticationConfiguration.CsrfHeader) : null,
            new LoginRequest { Email = user.Email!, Password = _password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(FindCookie(response, AuthenticationConfiguration.AuthenticationCookie));
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = await users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(persisted);
        Assert.Equal(0, await users.GetAccessFailedCountAsync(persisted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_WhenCsrfIsMissingOrInvalid_ShouldKeepSession(bool invalid)
    {
        var user = await CreateUserAsync();
        using var client = CreateClient();
        using var login = await LoginAsync(client, user.Email!, _password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var response = await SendAsync(client, "/api/auth/logout",
            invalid ? new CsrfResponse("invalid-token", AuthenticationConfiguration.CsrfHeader) : null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Csrf_FromAnotherClient_ShouldBeRejected()
    {
        var user = await CreateUserAsync();
        using var first = CreateClient();
        using var second = CreateClient();
        var token = await GetCsrfAsync(first);
        await GetCsrfAsync(second);
        using var response = await SendAsync(second, "/api/auth/login", token,
            new LoginRequest { Email = user.Email!, Password = _password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RepeatedFailures_ShouldLockAccountAndRejectCorrectPassword()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient();
        var csrf = await GetCsrfAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await SendAsync(client, "/api/auth/login", csrf,
                new LoginRequest { Email = user.Email!, Password = "aA!9" + Guid.NewGuid().ToString("N") });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var persisted = await users.FindByIdAsync(user.Id.ToString());
            Assert.NotNull(persisted);
            Assert.True(await users.IsLockedOutAsync(persisted));
        }
        using var correct = await SendAsync(client, "/api/auth/login", csrf,
            new LoginRequest { Email = user.Email!, Password = _password });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Null(FindCookie(correct, AuthenticationConfiguration.AuthenticationCookie));
    }

    [Fact]
    public async Task LoginRateLimit_ShouldReturn429AndRetryAfter()
    {
        using var client = CreateClient();
        var csrf = await GetCsrfAsync(client);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await SendAsync(client, "/api/auth/login", csrf,
                new LoginRequest { Email = "absent@example.test", Password = _password });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var rejected = await SendAsync(client, "/api/auth/login", csrf,
            new LoginRequest { Email = "absent@example.test", Password = _password });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task Me_ShouldExposeOnlyBasicIdentityFields()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient();
        using var login = await LoginAsync(client, user.Email!, _password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(new[] { "email", "id", "roles" },
            json.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.False(body.Contains(_password, StringComparison.Ordinal));
        Assert.False(body.Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains("securityStamp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AuthenticatedUserWithoutRole_ShouldReceive403WithoutHtmlRedirect()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient();
        using var login = await LoginAsync(client, user.Email!, _password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var response = await client.GetAsync("/test/auth/admin");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProductionHttps_ShouldIssueSecureAuthAndCsrfCookies()
    {
        using var factory = new AuthTestFactory(production: true);
        var user = await CreateUserAsync(factory);
        using var client = CreateClient(factory, https: true);
        using var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var csrfCookie = FindCookie(csrf, "InventoryManagement.Csrf");
        Assert.NotNull(csrfCookie);
        Assert.True(csrfCookie.Secure);
        using var login = await LoginAsync(client, user.Email!, _password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var authCookie = FindCookie(login, AuthenticationConfiguration.AuthenticationCookie);
        Assert.NotNull(authCookie);
        Assert.True(authCookie.Secure);
        Assert.True(authCookie.HttpOnly);
    }

    [Fact]
    public void SessionOptions_ShouldHaveBoundedLifetimeAndServerSideRevocation()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        Assert.Equal(TimeSpan.FromMinutes(30), options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);
        Assert.NotNull(options.SessionStore);
    }

    [Fact]
    public async Task PublicRegister_ShouldNotExist()
    {
        using var client = await _factory.CreateRoleClientAsync(ApplicationRoles.Admin);
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient(AuthTestFactory? factory = null, bool https = false)
        => (factory ?? _factory).CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri(https ? "https://localhost" : "http://localhost")
        });

    private async Task<ApplicationUser> CreateUserAsync(AuthTestFactory? factory = null)
    {
        using var scope = (factory ?? _factory).Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = "auth-" + Guid.NewGuid().ToString("N") + "@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email };
        var result = await users.CreateAsync(user, _password);
        Assert.True(result.Succeeded, "SQLite test account creation failed.");
        return user;
    }

    private static async Task<CsrfResponse> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.RequestToken));
        return token;
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
        => await SendAsync(client, "/api/auth/login", await GetCsrfAsync(client),
            new LoginRequest { Email = email, Password = password });

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string endpoint,
        CsrfResponse? csrf, LoginRequest? login = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (csrf is not null) request.Headers.Add(csrf.HeaderName, csrf.RequestToken);
        if (login is not null) request.Content = JsonContent.Create(login);
        return SendAndDisposeAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request) return await client.SendAsync(request);
    }

    private static SetCookieHeaderValue? FindCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers)) return null;
        return headers.Select(header => SetCookieHeaderValue.Parse(header))
            .FirstOrDefault(cookie => cookie.Name.ToString() == name);
    }

    public void Dispose() => _factory.Dispose();

    private sealed class AuthTestFactory(bool production = false) : CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (production) builder.UseEnvironment("Production");
            builder.ConfigureServices(services => services.AddControllers()
                .AddApplicationPart(typeof(AuthAuthorizationProbeController).Assembly));
        }
    }
}

[ApiController]
[Route("test/auth/admin")]
[Authorize(Roles = "Admin")]
public class AuthAuthorizationProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using WebAPI.Infrastructure.Security;

namespace Tests.Security;

public class HttpContextCurrentUserTests
{
    [Fact]
    public void UserId_WhenHttpContextIsNull_ShouldReturnNull()
    {
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor());

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_WhenUserIsAnonymous_ShouldReturnNull()
    {
        var currentUser = CreateCurrentUser(new ClaimsIdentity());

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_WhenAuthenticatedIdentifierIsValid_ShouldReturnGuid()
    {
        var userId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));

        Assert.Equal(userId, currentUser.UserId);
    }

    [Fact]
    public void UserId_WhenAuthenticatedIdentifierIsMissing_ShouldReturnNull()
    {
        var currentUser = CreateCurrentUser(new ClaimsIdentity("Test"));

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_WhenAuthenticatedIdentifierIsInvalid_ShouldReturnNull()
    {
        var currentUser = CreateCurrentUser(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")], "Test"));

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_WhenAuthenticatedIdentifierIsEmptyGuid_ShouldReturnNull()
    {
        var currentUser = CreateCurrentUser(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString())], "Test"));

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_WhenAnonymousIdentityHasGuidClaim_ShouldReturnNull()
    {
        var currentUser = CreateCurrentUser(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())]));

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_ShouldIgnoreIdentifierFromAnAnonymousIdentity()
    {
        var userId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())]),
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));

        Assert.Equal(userId, currentUser.UserId);
    }

    [Fact]
    public void UserId_ShouldNotCombineAuthenticatedAndAnonymousClaims()
    {
        var currentUser = CreateCurrentUser(
            new ClaimsIdentity("Test"),
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())]));

        Assert.Null(currentUser.UserId);
    }

    private static HttpContextCurrentUser CreateCurrentUser(params ClaimsIdentity[] identities)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identities) }
        };

        return new HttpContextCurrentUser(accessor);
    }
}

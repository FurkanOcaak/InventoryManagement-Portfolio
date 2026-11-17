using System.Security.Claims;
using Business.Common.Security;

namespace WebAPI.Infrastructure.Security;

public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var identity = httpContextAccessor.HttpContext?.User.Identities
                .FirstOrDefault(identity => identity.IsAuthenticated);
            var identifier = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
                ? userId
                : null;
        }
    }
}

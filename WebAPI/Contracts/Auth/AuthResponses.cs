namespace WebAPI.Contracts.Auth;

public sealed record CurrentUserResponse(Guid Id, string? Email, IList<string> Roles);

public sealed record CsrfResponse(string RequestToken, string HeaderName);

using System.Security.Cryptography;
using DataAccess.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace WebAPI.Infrastructure.Security;

public sealed class UnknownUserPasswordVerifier
{
    private readonly PasswordHasher<ApplicationUser> _hasher;
    private readonly ApplicationUser _placeholder = new();
    private readonly string _hash;

    public UnknownUserPasswordVerifier(IOptions<PasswordHasherOptions> options)
    {
        _hasher = new PasswordHasher<ApplicationUser>(options);
        _hash = _hasher.HashPassword(_placeholder,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public void Verify(string password)
    {
        _hasher.VerifyHashedPassword(_placeholder, _hash, password);
    }
}

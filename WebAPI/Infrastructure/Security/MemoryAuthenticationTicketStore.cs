using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace WebAPI.Infrastructure.Security;

public sealed class MemoryAuthenticationTicketStore(IMemoryCache cache) : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = "IdentitySession:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        cache.Set(key, TicketSerializer.Default.Serialize(ticket),
            ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddMinutes(30));
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var ticket = cache.TryGetValue<byte[]>(key, out var serialized) && serialized is not null
            ? TicketSerializer.Default.Deserialize(serialized)
            : null;
        return Task.FromResult(ticket);
    }

    public Task RemoveAsync(string key)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }
}

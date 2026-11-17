using Business.Common.Security;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Identity;

public sealed class CurrentCustomer(
    ICurrentUser currentUser, InventoryDbContext context,
    UserManager<ApplicationUser> users) : ICurrentCustomer
{
    public async Task<Guid?> GetCustomerIdAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId) return null;
        var user = await users.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (user?.CustomerId is not Guid customerId
            || !await users.IsInRoleAsync(user, ApplicationRoles.Customer)) return null;
        // The global filter rejects inactive customers; never use a claim or request ID here.
        return await context.Customers.AsNoTracking().AnyAsync(
            customer => customer.Id == customerId, cancellationToken) ? customerId : null;
    }
}

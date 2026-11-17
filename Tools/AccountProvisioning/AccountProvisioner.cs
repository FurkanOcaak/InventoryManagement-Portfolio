using System.ComponentModel.DataAnnotations;
using System.Data;
using Business.Common.Security;
using DataAccess.Identity;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace InventoryManagement.AccountProvisioning;

public enum AccountProvisioningStatus
{
    Created, NotDevelopment, NotConfirmed, InvalidInput, UnsafeTarget,
    ExistingAccount, CustomerUnavailable, Failed
}

public sealed record AccountProvisioningResult(AccountProvisioningStatus Status, Guid? UserId = null);

// Only registered by the local CLI (or explicitly in isolated tests), never by WebAPI.
public sealed class AccountProvisioner(
    InventoryDbContext context, UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles, IAccountProvisioningTarget target,
    IHostEnvironment environment)
{
    public static bool IsAllowedRole(string? role)
        => role is ApplicationRoles.WarehouseStaff or ApplicationRoles.Customer;

    public async Task<bool> IsValidInputAsync(string? email, string? password, string role, Guid? customerId)
    {
        if (!environment.IsDevelopment() || !IsAllowedRole(role)) return false;
        if ((role == ApplicationRoles.Customer && (customerId is null || customerId == Guid.Empty))
            || (role == ApplicationRoles.WarehouseStaff && customerId is not null)) return false;
        email = email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || email.Any(char.IsControl)
            || !new EmailAddressAttribute().IsValid(email)
            || password is null || password.Length is < 20 or > 1024) return false;
        var candidate = new ApplicationUser { UserName = email, Email = email };
        foreach (var validator in users.PasswordValidators)
            if (!(await validator.ValidateAsync(users, candidate, password)).Succeeded) return false;
        return true;
    }

    public async Task<AccountProvisioningResult> ProvisionAsync(string? email, string? password,
        string role, Guid? customerId, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment()) return new(AccountProvisioningStatus.NotDevelopment);
        if (!confirmed) return new(AccountProvisioningStatus.NotConfirmed);
        if (!await IsValidInputAsync(email, password, role, customerId))
            return new(AccountProvisioningStatus.InvalidInput);

        // Raw provider/Identity errors must never reach the console or logs.
        try
        {
            if (!await target.ValidateAsync(cancellationToken)) return new(AccountProvisioningStatus.UnsafeTarget);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            try
            {
                var normalizedEmail = users.NormalizeEmail(email!.Trim());
                var normalizedName = users.NormalizeName(email.Trim());
                // Serializable read protects the email check as well as the unique username index.
                if (await context.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail
                    || user.NormalizedUserName == normalizedName, cancellationToken))
                    return new(AccountProvisioningStatus.ExistingAccount);

                if (customerId is Guid id && (!await context.Customers.AsNoTracking()
                    .AnyAsync(customer => customer.Id == id, cancellationToken)
                    || await context.Users.AnyAsync(user => user.CustomerId == id, cancellationToken)))
                    return new(AccountProvisioningStatus.CustomerUnavailable);

                var existingRole = await roles.FindByNameAsync(role);
                if (existingRole is not null && (existingRole.Name != role
                    || existingRole.NormalizedName != roles.NormalizeKey(role)))
                    return new(AccountProvisioningStatus.Failed);
                if (existingRole is null && !(await roles.CreateAsync(
                    new IdentityRole<Guid>(role) { Id = Guid.NewGuid() })).Succeeded)
                    return await RejectAsync();

                var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email.Trim(),
                    Email = email.Trim(), CustomerId = customerId, LockoutEnabled = true };
                if (!(await users.CreateAsync(user, password!)).Succeeded) return await RejectAsync();
                if (!(await users.AddToRoleAsync(user, role)).Succeeded) return await RejectAsync();
                var assignedRoles = await users.GetRolesAsync(user);
                if (assignedRoles.Count != 1 || assignedRoles[0] != role
                    || string.IsNullOrEmpty(user.PasswordHash) || !await users.CheckPasswordAsync(user, password!))
                    return await RejectAsync();

                await transaction.CommitAsync(cancellationToken);
                return new(AccountProvisioningStatus.Created, user.Id);

                async Task<AccountProvisioningResult> RejectAsync()
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    context.ChangeTracker.Clear();
                    return new(AccountProvisioningStatus.Failed);
                }
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch { /* Never retry a write or expose provider diagnostics. */ }
                context.ChangeTracker.Clear();
                return new(AccountProvisioningStatus.Failed);
            }
        }
        catch
        {
            return new(AccountProvisioningStatus.Failed);
        }
    }
}

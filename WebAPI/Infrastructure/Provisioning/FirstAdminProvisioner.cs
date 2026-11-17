using System.ComponentModel.DataAnnotations;
using System.Data;
using Business.Common.Security;
using DataAccess.Identity;
using DataAccess.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace WebAPI.Infrastructure.Provisioning;

public enum ProvisioningStatus
{
    Created,
    NotDevelopment,
    NotConfirmed,
    ExistingUsers,
    InvalidInput,
    Failed
}

public sealed record ProvisioningResult(ProvisioningStatus Status, Guid? UserId = null,
    IReadOnlyList<string>? ErrorCodes = null);

// Registered only by the local developer CLI, never by the Web API host.
public sealed class FirstAdminProvisioner(
    InventoryDbContext context,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    SignInManager<ApplicationUser> signInManager,
    IHostEnvironment environment)
{
    public const string AdminRole = ApplicationRoles.Admin;

    public async Task<IReadOnlyList<string>> ValidateInputAsync(string? email, string? password)
    {
        if (!environment.IsDevelopment()) return ["DevelopmentRequired"];
        var errors = new List<string>();
        email = email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256
            || !new EmailAddressAttribute().IsValid(email)) errors.Add("InvalidEmail");
        if (password is null || password.Length < 20) errors.Add("PasswordRequires20Characters");
        if (errors.Count > 0) return errors;

        var candidate = new ApplicationUser { UserName = email, Email = email };
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, candidate, password!);
            errors.AddRange(result.Errors.Select(error => error.Code));
        }
        return errors.Distinct().ToArray();
    }

    public async Task<ProvisioningResult> ProvisionAsync(string? email, string? password,
        bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment()) return new(ProvisioningStatus.NotDevelopment);
        if (!confirmed) return new(ProvisioningStatus.NotConfirmed);
        if (await context.Users.AnyAsync(cancellationToken)) return new(ProvisioningStatus.ExistingUsers);

        var errors = await ValidateInputAsync(email, password);
        if (errors.Count > 0) return new(ProvisioningStatus.InvalidInput, ErrorCodes: errors);

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            // Repeat inside the transaction to protect concurrent bootstrap attempts.
            if (await context.Users.AnyAsync(cancellationToken))
                return new(ProvisioningStatus.ExistingUsers);

            if (!await roles.RoleExistsAsync(AdminRole))
            {
                var roleResult = await roles.CreateAsync(new IdentityRole<Guid>(AdminRole)
                {
                    Id = Guid.NewGuid()
                });
                if (!roleResult.Succeeded) return await RejectAsync(roleResult);
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email!.Trim(), Email = email.Trim(),
                LockoutEnabled = true
            };
            var created = await users.CreateAsync(user, password!);
            if (!created.Succeeded) return await RejectAsync(created);
            var assigned = await users.AddToRoleAsync(user, AdminRole);
            if (!assigned.Succeeded) return await RejectAsync(assigned);

            if (!await users.IsInRoleAsync(user, AdminRole)
                || string.IsNullOrEmpty(user.PasswordHash)
                || !await users.CheckPasswordAsync(user, password!)
                || !(await signInManager.CheckPasswordSignInAsync(user, password!, false)).Succeeded)
                throw new InvalidOperationException("Provisioning verification failed.");

            await transaction.CommitAsync(cancellationToken);
            return new(ProvisioningStatus.Created, user.Id);
        }
        catch
        {
            // Never log exceptions: provider errors may contain identifying information.
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { /* An interrupted connection may already have rolled back. Do not retry. */ }
            context.ChangeTracker.Clear();
            return new(ProvisioningStatus.Failed, ErrorCodes: ["CheckDatabaseBeforeRetry"]);
        }

        async Task<ProvisioningResult> RejectAsync(IdentityResult result)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            return new(ProvisioningStatus.Failed,
                ErrorCodes: result.Errors.Select(error => error.Code).Distinct().ToArray());
        }
    }
}

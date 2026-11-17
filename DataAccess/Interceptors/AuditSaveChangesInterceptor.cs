using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Entities.Common;
using Business.Common.Security;

namespace DataAccess.Interceptors;

public class AuditSaveChangesInterceptor(ICurrentUser? currentUser = null) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAudit(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var utcNow = DateTime.UtcNow;
        // Resolve the current actor per save, never cache a user's identity.
        // Non-HTTP tools may have no current user; retain existing attribution.
        var userId = currentUser?.UserId is Guid id && id != Guid.Empty ? id : (Guid?)null;

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty)
                {
                    entry.Entity.Id = Guid.NewGuid();
                }

                entry.Entity.CreatedAtUtc = utcNow;
                entry.Entity.UpdatedAtUtc = null;
                entry.Entity.DeactivatedAtUtc = null;
                if (userId.HasValue)
                {
                    entry.Entity.CreatedByUserId = userId;
                }
            }
            else if (entry.State == EntityState.Deleted)
            {
                // Discard pending field edits before cancelling DELETE. Otherwise
                // EF's next change detection can mark those columns modified again.
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;

                // Explicitly mark audit columns after switching to Unchanged.
                entry.Property(x => x.IsActive).CurrentValue = false;
                entry.Property(x => x.IsActive).IsModified = true;

                entry.Property(x => x.DeactivatedAtUtc).CurrentValue = utcNow;
                entry.Property(x => x.DeactivatedAtUtc).IsModified = true;

                entry.Property(x => x.UpdatedAtUtc).CurrentValue = utcNow;
                entry.Property(x => x.UpdatedAtUtc).IsModified = true;
                if (userId.HasValue)
                {
                    entry.Property(x => x.DeactivatedByUserId).CurrentValue = userId;
                    entry.Property(x => x.DeactivatedByUserId).IsModified = true;
                    entry.Property(x => x.UpdatedByUserId).CurrentValue = userId;
                    entry.Property(x => x.UpdatedByUserId).IsModified = true;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                // Do not overwrite identity or creation audit fields.
                entry.Property(x => x.Id).IsModified = false;
                entry.Property(x => x.CreatedAtUtc).IsModified = false;
                entry.Property(x => x.CreatedByUserId).IsModified = false;

                entry.Property(x => x.UpdatedAtUtc).CurrentValue = utcNow;
                entry.Property(x => x.UpdatedAtUtc).IsModified = true;
                if (userId.HasValue)
                {
                    entry.Property(x => x.UpdatedByUserId).CurrentValue = userId;
                    entry.Property(x => x.UpdatedByUserId).IsModified = true;
                }
            }
        }
    }
}

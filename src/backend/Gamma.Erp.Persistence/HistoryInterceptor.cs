using Gamma.Erp.Models.Base;
using Gamma.Erp.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gamma.Erp.Persistence;

/// <summary>
/// The rules for every table, on every save:
/// <list type="bullet">
/// <item>Nothing is ever deleted: a delete switches the row off (<see cref="Row.Active"/> = false).</item>
/// <item>A change first copies the row as it was stored into a new, disabled row of the same table; the row keeps its id and points
/// at the copy (<see cref="Row.IdChange"/>), and the copy points at the copy before it, back to the first.</item>
/// <item>A new row and each change are stamped with when and by whom, and new rows get their id and
/// the request's tenant when they have none.</item>
/// <item>A disabled row (deleted, or a history copy) never changes, except by being switched back on.</item>
/// </list>
/// </summary>
public sealed class HistoryInterceptor(ITenantContext tenant, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<Row>().ToList())
        {
            var row = entry.Entity;
            switch (entry.State)
            {
                case EntityState.Added:
                    row.Id = row.Id == Guid.Empty ? Guid.CreateVersion7() : row.Id;
                    row.TenantId = row.TenantId == Guid.Empty ? tenant.TenantId ?? Guid.Empty : row.TenantId;
                    (row.CreatedOn, row.CreatedBy) = (now, row.CreatedBy ?? tenant.UserId);
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    row.Active = false;
                    goto case EntityState.Modified;

                case EntityState.Modified:
                    // A disabled row (deleted, or a history copy) only ever changes by being switched back on.
                    if (!(bool)entry.OriginalValues[nameof(Row.Active)]! && !row.Active)
                    {
                        throw new InvalidOperationException($"{entry.Metadata.GetTableName()} {row.Id} is disabled and cannot change.");
                    }

                    var copy = (Row)Activator.CreateInstance(entry.Metadata.ClrType, nonPublic: true)!;
                    context.Entry(copy).CurrentValues.SetValues(entry.OriginalValues);
                    (copy.Id, copy.Active) = (Guid.CreateVersion7(), false);
                    context.Add(copy);
                    (row.IdChange, row.CreatedOn, row.CreatedBy) = (copy.Id, now, tenant.UserId ?? row.CreatedBy);
                    break;
            }
        }
    }
}

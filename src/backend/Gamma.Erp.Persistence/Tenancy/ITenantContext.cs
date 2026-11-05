namespace Gamma.Erp.Persistence.Tenancy;

/// <summary>
/// The company (tenant) the current request works in. Set once per request from the signed-in
/// user's session; every database connection opened afterwards is restricted to this tenant by
/// PostgreSQL row-level security.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    /// <summary>The signed-in user, when there is one.</summary>
    Guid? UserId { get; }

    bool HasTenant => TenantId.HasValue;
}

/// <summary>Scoped, mutable implementation filled in by the authentication middleware.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public Guid? UserId { get; private set; }

    public void Set(Guid tenantId, Guid? userId)
    {
        if (TenantId.HasValue && TenantId != tenantId)
        {
            throw new InvalidOperationException("The tenant of a request cannot change once it is set.");
        }

        TenantId = tenantId;
        UserId = userId;
    }
}

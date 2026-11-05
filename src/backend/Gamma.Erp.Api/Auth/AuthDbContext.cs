using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Api.Auth;

/// <summary>OpenIddict's own tables (clients, authorizations, scopes, tokens) in the auth schema: sign-in data, no business data, no tenant.</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public const string Schema = "auth";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }
}

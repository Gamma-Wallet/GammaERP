using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gamma.Erp.Api.Auth;

/// <summary>For `dotnet ef` only: builds the sign-in model to create its migrations (no database is opened).</summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AuthDbContext>()
        .UseNpgsql("Host=design-time-only", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AuthDbContext.Schema)).UseOpenIddict().Options);
}

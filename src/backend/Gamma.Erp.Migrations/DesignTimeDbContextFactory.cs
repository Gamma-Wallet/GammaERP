using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gamma.Erp.Migrations;

/// <summary>Builds the context for "dotnet ef" (no database is needed to add a migration).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    public ErpDbContext CreateDbContext(string[] args) =>
        new(PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), "Host=localhost;Database=design_time").Options is var options
            ? (DbContextOptions<ErpDbContext>)options : throw new InvalidOperationException());
}

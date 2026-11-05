using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Persistence;

/// <summary>
/// The Gamma ERP database: every model of Gamma.Erp.Models as its tbl_ table (see <see cref="ModelMapping"/>), in one PostgreSQL schema.
/// One context for everything, so a posting that touches several tables is one transaction.
/// </summary>
public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public const string MigrationsAssembly = "Gamma.Erp.Migrations";

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ModelMapping.Apply(modelBuilder);
}

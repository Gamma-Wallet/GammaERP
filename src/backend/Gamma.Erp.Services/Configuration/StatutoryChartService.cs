using Gamma.Erp.Models.Charts;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Configuration;

/// <summary>The company's statutory chart category, or null when none is marked.</summary>
public sealed record StatutoryCategoryDto(Guid? Id);

/// <summary>
/// Which of the company's chart categories is statutory (tbl_chart_category.is_statutory): the chart the reports read, the one the account mapping maps the
/// others into. The reports read one, so marking a category statutory takes the mark from the company's others.
/// </summary>
public sealed class StatutoryChartService(ErpDbContext db, Visibility visibility)
{
    public async Task<StatutoryCategoryDto> StatutoryAsync(CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        return new(await db.Set<ChartCategory>().AsNoTracking().Where(c => c.OwnerId == company && c.IsStatutory && c.Active).Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false));
    }

    public async Task<StatutoryCategoryDto> SetAsync(Guid id, bool statutory, CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<ChartCategory>().Where(c => c.OwnerId == company && (c.Id == id || c.IsStatutory)).ToListAsync(ct).ConfigureAwait(false);
        if (rows.All(c => c.Id != id))
        {
            throw ValidationException.For("id", "The chart category is not one of the company's.");
        }

        foreach (var row in rows)
        {
            row.IsStatutory = row.Id == id ? statutory : row.IsStatutory && !statutory;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await StatutoryAsync(ct).ConfigureAwait(false);
    }
}

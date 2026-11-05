using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Entities;

/// <summary>The cost centre tree's own rule (the founder's, 2026-10-29): a company is the root of its tree or directly under it, never under a company.</summary>
public static class CostCenterRules
{
    public static Func<ElementDto, CancellationToken, Task> CompanyLevel(ErpDbContext db) => async (dto, ct) =>
    {
        ArgumentNullException.ThrowIfNull(dto);
        var keepsBooks = await db.Set<CostCenterType>().AsNoTracking().Where(t => t.Id == dto.TypeId).Select(t => t.KeepsBooks).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (!keepsBooks || dto.IdSup is not { } parentId)
        {
            return;
        }

        var parent = await (from c in db.Set<CostCenter>().AsNoTracking() join t in db.Set<CostCenterType>().AsNoTracking() on c.TypeId equals t.Id
                            where c.Id == parentId select new { c.IdSup, t.KeepsBooks }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (parent is null || parent.IdSup is not null || parent.KeepsBooks)
        {
            throw ValidationException.For("idSup", $"{ErrorCodes.CompanyLevelError}: a company is the root of its tree or directly under it, never under another company.");
        }
    };
}

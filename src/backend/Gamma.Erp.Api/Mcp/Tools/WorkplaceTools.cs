using System.ComponentModel;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>A place the user may work at, as Claude lists it.</summary>
public sealed record WorkplaceDto(Guid Id, string? Code, string? Name, bool Chosen);

/// <summary>Who is signed in through Claude and where they work: the same cost centres as the screen after sign-in.</summary>
[McpServerToolType]
public sealed class WorkplaceTools(ErpDbContext db, RequestContext request, McpWorkplaces workplaces)
{
    [McpServerTool(Name = "list_workplaces", Title = "List where I work", ReadOnly = true)]
    [Description("The signed-in user and the places (companies and cost centres) they may work at, marking the one chosen now. Call it first; then choose_workplace if more than one is offered and none is chosen.")]
    public async Task<object> ListWorkplacesAsync(CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().Where(u => u.Id == request.UserId).Select(u => new { u.Username }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var offered = await OfferedAsync(ct).ConfigureAwait(false);
        return new { user = user?.Username, administrator = request.IsAdministrator, workplaces = offered };
    }

    [McpServerTool(Name = "choose_workplace", Title = "Choose where I work", Idempotent = true)]
    [Description("Chooses the place to work at, by its id from list_workplaces. Everything after works there, as on the screens after choosing a cost centre.")]
    public async Task<object> ChooseWorkplaceAsync([Description("The workplace's id, from list_workplaces.")] Guid id, CancellationToken ct)
    {
        var linked = await db.Set<InterUserCostCenter>().AsNoTracking().Where(l => l.UserId == request.UserId && l.CostCenterId != null).Select(l => l.CostCenterId!.Value)
            .ToListAsync(ct).ConfigureAwait(false);
        if (!await Workplaces.MayWorkAtAsync(db, linked, id, ct).ConfigureAwait(false))
        {
            throw new ModelContextProtocol.McpException("That is not one of your workplaces: take one from list_workplaces.");
        }

        workplaces.Choose(request.UserId!.Value, id);
        return new { chosen = id, message = "Chosen. The next tools work there." };
    }

    private async Task<IReadOnlyList<WorkplaceDto>> OfferedAsync(CancellationToken ct)
    {
        var linked = await db.Set<InterUserCostCenter>().AsNoTracking().Where(l => l.UserId == request.UserId && l.CostCenterId != null).Select(l => l.CostCenterId!.Value)
            .ToListAsync(ct).ConfigureAwait(false);
        var offered = await Workplaces.OfferedAsync(db, linked, ct).ConfigureAwait(false);
        return [.. offered.Select(o => new WorkplaceDto(o.Place.Id, o.Place.Code, o.Place.Name.In(TranslatedText.DefaultLanguage), o.Place.Id == request.NodeId))];
    }
}

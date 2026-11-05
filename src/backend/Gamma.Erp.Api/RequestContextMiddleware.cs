using System.Security.Claims;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Services;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Gamma.Erp.Api;

/// <summary>
/// Builds each API request's <see cref="RequestContext"/> from the token and the database: the corporate and the user from the token; the
/// roles from the user's categories (tbl_inter_user_category), administrator when one of them is marked so; the node from the
/// <see cref="NodeHeader"/> header, only one of the user's cost centres (tbl_inter_user_cost_center), or the
/// only one when the user has just one. The tenant is set first, so row-level security applies to everything the request reads.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public const string NodeHeader = "X-Gamma-Node";

    public async Task InvokeAsync(HttpContext http, ErpDbContext db, TenantContext tenant, Mcp.McpWorkplaces workplaces)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(workplaces);
        var mcp = http.Request.Path.StartsWithSegments(Mcp.McpSetup.Path, StringComparison.Ordinal);
        if (http.User.Identity?.IsAuthenticated != true || !(mcp || http.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal)))
        {
            await next(http).ConfigureAwait(false);
            return;
        }

        var ct = http.RequestAborted;
        if (!Guid.TryParse(http.User.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId)
            || !Guid.TryParse(http.User.FindFirstValue(AuthEndpoints.TenantClaim), out var tenantId))
        {
            await Results.Problem("The token does not name a user and a corporate.", statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(http).ConfigureAwait(false);
            return;
        }

        tenant.Set(tenantId, userId);
        var roles = await (from l in db.Set<InterUserCategory>().AsNoTracking() join c in db.Set<UserCategory>().AsNoTracking() on l.CategoryId equals c.Id
                           where l.ElementId == userId select new { c.Id, c.IsAdministrator }).ToListAsync(ct).ConfigureAwait(false);
        var nodes = await db.Set<InterUserCostCenter>().AsNoTracking().Where(l => l.UserId == userId && l.CostCenterId != null).Select(l => l.CostCenterId!.Value)
            .ToListAsync(ct).ConfigureAwait(false);
        Guid node;
        if (http.Request.Headers.TryGetValue(NodeHeader, out var header))
        {
            // One of the user's cost centres or any below them (the founder, 2026-10-31), never above.
            if (!Guid.TryParse(header, out node) || !await Auth.Workplaces.MayWorkAtAsync(db, nodes, node, ct).ConfigureAwait(false))
            {
                await Results.Problem($"The node in {NodeHeader} is not one of your cost centres.", statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(http).ConfigureAwait(false);
                return;
            }
        }
        else if (nodes.Count == 1)
        {
            node = nodes[0];
        }
        else if (mcp)
        {
            // Claude chooses where to work with a tool (choose_workplace); until then the tools that need a place say so.
            node = workplaces.Of(userId) is { } chosen && await Auth.Workplaces.MayWorkAtAsync(db, nodes, chosen, ct).ConfigureAwait(false) ? chosen : Guid.Empty;
        }
        else
        {
            await Results.Problem(nodes.Count == 0 ? "You are not linked to any cost centre." : $"Choose the cost centre you work at with the {NodeHeader} header.",
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(http).ConfigureAwait(false);
            return;
        }

        http.Items[typeof(RequestContext)] = new RequestContext(tenantId, userId, node, roles.Select(r => r.Id).ToHashSet(), roles.Any(r => r.IsAdministrator));
        await next(http).ConfigureAwait(false);
    }
}

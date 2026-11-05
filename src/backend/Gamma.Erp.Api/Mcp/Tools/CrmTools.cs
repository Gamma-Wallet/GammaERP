using System.ComponentModel;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Opportunities;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>The pipeline (CRM board) through Claude, behind the Pipeline screen as its routes are; nothing moves by itself.</summary>
[McpServerToolType]
public sealed class CrmTools(OpportunityService opportunities, ErpDbContext db, RequestContext request, Visibility visibility)
{
    private const string Board = "opportunity-board";

    [McpServerTool(Name = "pipeline", Title = "The pipeline board", ReadOnly = true)]
    [Description("The CRM pipeline: the company's stages (chance %, outcome open/won/lost) and the opportunities in each, with client, amount, currency, expected close, documents and invoiced amount. Use move_opportunity to move one.")]
    public Task<object> PipelineAsync(CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, Board, ct).ConfigureAwait(false);
            var board = await opportunities.BoardAsync(Registers.English, ct).ConfigureAwait(false);
            return board.Stages.Select(s => new
            {
                stageId = s.Id, stage = s.Name, s.Chance, outcome = s.Outcome switch { 1 => "won", 2 => "lost", _ => "open" },
                opportunities = board.Cards.Where(c => c.StageId == s.Id).Select(c => new { c.Id, c.Code, c.Name, c.Partner, c.Amount, c.Currency, c.ExpectedClose, c.Documents, c.Invoiced }),
            }).ToList();
        });

    [McpServerTool(Name = "move_opportunity", Title = "Move an opportunity", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Moves an opportunity to another stage of the pipeline, as dragging it on the board does; a lost stage needs the reason. First tell the user which opportunity goes to which stage and get an explicit yes; then confirm=true.")]
    public Task<object> MoveOpportunityAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The opportunity: id or exact code.")] string opportunity,
        [Description("The stage: id or name, from pipeline.")] string stage,
        [Description("Why it was lost; needed for a lost stage.")] string? lostReason = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "which opportunity moves to which stage");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, Board, ct).ConfigureAwait(false);
            var id = await Registers.ElementAsync(s => Registers.Opportunities(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct),
                opportunity, "opportunity").ConfigureAwait(false);
            var stages = (await opportunities.FormAsync(Registers.English, ct).ConfigureAwait(false)).Stages;
            var key = stage.Trim();
            var target = stages.FirstOrDefault(s => Registers.Is(s.Id, key) || string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException($"There is no stage \"{key}\". The stages: {string.Join(", ", stages.Select(s => s.Name))}.");
            await opportunities.MoveAsync(id, target.Id, lostReason, ct).ConfigureAwait(false);
            return new { moved = id, stage = target.Name, lostReason = target.Outcome == 2 ? lostReason : null };
        });
}

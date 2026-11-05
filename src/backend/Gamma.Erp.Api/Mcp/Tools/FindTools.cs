using System.ComponentModel;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using CostCenters = Gamma.Erp.Models.CostCenters;
using Currencies = Gamma.Erp.Models.Currencies;
using Opportunities = Gamma.Erp.Models.Opportunities;
using Partners = Gamma.Erp.Models.Partners;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>Finding rows of the registers by code or name, as the screens' lookups do: only what the user's rights on each category show.</summary>
[McpServerToolType]
public sealed class FindTools(ErpDbContext db, RequestContext request, Visibility visibility)
{
    private const string SearchHelp = "Part of the code or the name; empty lists everything (a page at a time).";
    private const string CategoryHelp = "Optional: only this category (its code, name or id).";
    private const string PageHelp = "Page number, from 1.";
    private const string SizeHelp = "Rows per page: 25 if not given, at most 100.";

    [McpServerTool(Name = "find_partners", Title = "Find clients and suppliers", ReadOnly = true)]
    [Description("Finds partners (clients, suppliers) by code or name. Returns a page of rows with id, code, name, type and categories, and the total. Use it to get a partner's id before writing a document or reading its open items.")]
    public Task<object> FindPartnersAsync([Description(SearchHelp)] string? search = null, [Description(CategoryHelp)] string? category = null,
        [Description(PageHelp)] int? page = null, [Description(SizeHelp)] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            return await Registers.FindAsync(Registers.Partners(db, request, visibility), db, search, category, "partner", page, pageSize, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "find_products", Title = "Find products and services", ReadOnly = true)]
    [Description("Finds products and services by code or name. Returns a page of rows with id, code, name, type and categories. Use it to get a product's id for a document line or a stock question.")]
    public Task<object> FindProductsAsync([Description(SearchHelp)] string? search = null, [Description(CategoryHelp)] string? category = null,
        [Description(PageHelp)] int? page = null, [Description(SizeHelp)] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            return await Registers.FindAsync(Registers.Products(db, request, visibility), db, search, category, "product", page, pageSize, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "find_accounts", Title = "Find accounts of the chart", ReadOnly = true)]
    [Description("Finds accounts of the chart of accounts by code or name (each country's chart keeps its own language). Returns id, code, name, type and the chart (category). Use it before account_ledger or a journal line.")]
    public Task<object> FindAccountsAsync([Description(SearchHelp)] string? search = null, [Description("Optional: only this chart (its code, name or id).")] string? chart = null,
        [Description(PageHelp)] int? page = null, [Description(SizeHelp)] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            return await Registers.FindAsync(Registers.Charts(db, request, visibility), db, search, chart, "chart", page, pageSize, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "find_cost_centers", Title = "Find cost centres", ReadOnly = true)]
    [Description("Finds cost centres (companies, warehouses, cash desks, banks, points of sale, tills, tables…) by code or name. Returns id, code, name, type, categories and the parent's id. Use it to pick a warehouse, a cash desk or a place under which to add one.")]
    public Task<object> FindCostCentersAsync([Description(SearchHelp)] string? search = null, [Description(CategoryHelp)] string? category = null,
        [Description(PageHelp)] int? page = null, [Description(SizeHelp)] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            return await Registers.FindAsync(Registers.CostCenters(db, request, visibility), db, search, category, "cost centre", page, pageSize, ct, async ids =>
                (await db.Set<CostCenters.CostCenter>().AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.IdSup }).ToListAsync(ct).ConfigureAwait(false))
                .ToDictionary(c => c.Id, c => (object)new { parentId = c.IdSup })).ConfigureAwait(false);
        });

    [McpServerTool(Name = "find_opportunities", Title = "Find opportunities", ReadOnly = true)]
    [Description("Finds opportunities (CRM) by code or name. Returns id, code, name, type, categories and the client, stage, amount, currency and expected close. For the whole board by stage use pipeline.")]
    public Task<object> FindOpportunitiesAsync([Description(SearchHelp)] string? search = null, [Description(CategoryHelp)] string? category = null,
        [Description(PageHelp)] int? page = null, [Description(SizeHelp)] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            return await Registers.FindAsync(Registers.Opportunities(db, request, visibility), db, search, category, "opportunity", page, pageSize, ct, async ids =>
            {
                var rows = await db.Set<Opportunities.Opportunity>().AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(ct).ConfigureAwait(false);
                var partnerIds = rows.Select(r => r.PartnerId).OfType<Guid>().Distinct().ToList();
                var stageIds = rows.Select(r => r.StageId).OfType<Guid>().Distinct().ToList();
                var currencyIds = rows.Select(r => r.CurrencyId).OfType<Guid>().Distinct().ToList();
                var partners = (await db.Set<Partners.Partner>().AsNoTracking().Where(p => partnerIds.Contains(p.Id)).ToListAsync(ct).ConfigureAwait(false))
                    .ToDictionary(p => p.Id, p => p.Name.In(Registers.English));
                var stages = (await db.Set<Opportunities.OpportunityStage>().AsNoTracking().Where(s => stageIds.Contains(s.Id)).ToListAsync(ct).ConfigureAwait(false))
                    .ToDictionary(s => s.Id, s => s.Name.In(Registers.English));
                var currencies = (await db.Set<Currencies.Currency>().AsNoTracking().Where(c => currencyIds.Contains(c.Id)).ToListAsync(ct).ConfigureAwait(false))
                    .ToDictionary(c => c.Id, c => c.Code);
                return rows.ToDictionary(r => r.Id, r => (object)new
                {
                    partner = r.PartnerId is { } p ? partners.GetValueOrDefault(p) : null, stage = r.StageId is { } s ? stages.GetValueOrDefault(s) : null, r.Amount,
                    currency = r.CurrencyId is { } c ? currencies.GetValueOrDefault(c) : null, r.ExpectedClose, r.LostReason,
                });
            }).ConfigureAwait(false);
        });
}

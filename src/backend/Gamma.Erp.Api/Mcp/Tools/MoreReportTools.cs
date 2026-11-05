using System.ComponentModel;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Opportunities;
using Gamma.Erp.Services.Partners;
using Gamma.Erp.Services.Posting;
using Gamma.Erp.Services.Reports;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>The reports of the menu that have no tool of their own, each behind its report in the user's menu, as its route.</summary>
[McpServerToolType]
public sealed class MoreReportTools(ReportService reports, StockReportService stock, OpenItems openItems, PartnerStatisticsService partners, OpportunityService opportunities,
    PostingService posting, DocumentService documents, ErpDbContext db, RequestContext request, Visibility visibility)
{
    [McpServerTool(Name = "report", Title = "More reports", ReadOnly = true)]
    [Description("""
        The other reports of the Reports menu, by name:
        - ageing: open invoices by how late they are, on a day (to).
        - vat-register: the VAT books of document categories (categories) in a period.
        - document-totals: documents' totals by type and VAT group for categories in a period.
        - contracts: the contracts of categories in a period, optionally of one partner.
        - item-tracking: a product's movements in a period (product).
        - purchase-prices: what a product was bought at in a period (product).
        - partner-ledger: a client's or supplier's documents and balance in a period (partner).
        - partner-statistics: one partner's figures (partner, optional period).
        - partner-ranking: the partners of a category ranked by their figures (categories: one partner category).
        - cost-center-ledger: a cash desk's, bank's or warehouse's movements in a period (costCenter).
        - cost-center-balance: a cost centre's balance up to a day (costCenter, optional to).
        - staff-ledger: an employee's movements in a period (employee).
        - opportunity-documents: the documents made from opportunities in a period, optionally at one stage.
        - unbalanced-scenarios: the company's posting scenarios that cannot balance.
        - vat-return-form: the VAT return in the company's configured form (boxes) for a period.
        Each needs its report in the user's menu.
        """)]
    public Task<object> ReportAsync(
        [Description("The report's name (see the list).")] string name,
        [Description("From (yyyy-mm-dd), where the report has a period.")] DateOnly? from = null,
        [Description("To (yyyy-mm-dd): the period's end, or the day.")] DateOnly? to = null,
        [Description("Document categories (codes, names or ids) for vat-register, document-totals, contracts; one partner category for partner-ranking.")] IReadOnlyList<string>? categories = null,
        [Description("A product (id or exact code) for item-tracking and purchase-prices.")] string? product = null,
        [Description("A partner (id or exact code) for partner-ledger, partner-statistics and contracts.")] string? partner = null,
        [Description("A cost centre (id or exact code) for cost-center-ledger and cost-center-balance.")] string? costCenter = null,
        [Description("An employee (id or exact code) for staff-ledger.")] string? employee = null,
        [Description("opportunity-documents: optional, a stage's id.")] Guid? stage = null,
        [Description("partner-ranking: page number, from 1.")] int? page = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var key = name.Trim().ToLowerInvariant();
            DateOnly From() => from ?? throw new McpException("Give from (yyyy-mm-dd).");
            DateOnly To() => to ?? DateOnly.FromDateTime(DateTime.Today);
            switch (key)
            {
                case "ageing":
                    await Registers.Report(visibility, ct, "ageing").ConfigureAwait(false);
                    return await openItems.AgeingAsync(To(), ct).ConfigureAwait(false);
                case "vat-register":
                    await Registers.Report(visibility, ct, "vat-register").ConfigureAwait(false);
                    return await reports.VatRegisterAsync(await DocumentCategoriesAsync(categories, ct).ConfigureAwait(false), From(), To(), Registers.English, ct).ConfigureAwait(false);
                case "document-totals":
                    await Registers.Report(visibility, ct, "document-totals").ConfigureAwait(false);
                    return await reports.DocumentTotalsAsync(await DocumentCategoriesAsync(categories, ct).ConfigureAwait(false), From(), To(), Registers.English, ct).ConfigureAwait(false);
                case "contracts":
                    await Registers.Report(visibility, ct, "contracts").ConfigureAwait(false);
                    return await reports.ContractsAsync(await DocumentCategoriesAsync(categories, ct).ConfigureAwait(false),
                        partner is null ? null : await PartnerAsync(partner, ct).ConfigureAwait(false), From(), To(), Registers.English, ct).ConfigureAwait(false);
                case "item-tracking":
                    await Registers.Report(visibility, ct, "item-tracking").ConfigureAwait(false);
                    return await stock.ItemTrackingAsync(await ProductAsync(product, ct).ConfigureAwait(false), From(), To(), Registers.English, ct).ConfigureAwait(false);
                case "purchase-prices":
                    await Registers.Report(visibility, ct, "purchase-prices").ConfigureAwait(false);
                    return await stock.PurchasePricesAsync(await ProductAsync(product, ct).ConfigureAwait(false), From(), To(), ct).ConfigureAwait(false);
                case "partner-ledger":
                    await Registers.Report(visibility, ct, "partner-ledger").ConfigureAwait(false);
                    return await partners.LedgerAsync(await PartnerAsync(partner, ct).ConfigureAwait(false), From(), To(), ct).ConfigureAwait(false);
                case "partner-statistics":
                    if (!await visibility.OpensScreenAsync(["entities", "report"], ["partners", "partner-statistics"], ct).ConfigureAwait(false)
                        && !await visibility.OpensScreenAsync("documents", ct).ConfigureAwait(false))
                    {
                        throw new McpException($"{ErrorCodes.NotAllowedError}: this screen is not in your menu.");
                    }

                    return (object?)await partners.StatisticsAsync(await PartnerAsync(partner, ct).ConfigureAwait(false), from, to, ct).ConfigureAwait(false)
                        ?? throw new McpException("There is no such partner that you may see.");
                case "partner-ranking":
                    await Registers.Report(visibility, ct, "partner-statistics").ConfigureAwait(false);
                    var partnerCategory = Registers.Category(await Registers.Partners(db, request, visibility).CategoriesAsync(ct).ConfigureAwait(false),
                        categories is { Count: 1 } one ? one[0] : throw new McpException("Give one partner category (e.g. Clients)."), "partner")!.Value;
                    return await partners.RankingAsync(partnerCategory, from, to, Math.Max(page ?? 1, 1), Registers.MaxPage, ct).ConfigureAwait(false);
                case "cost-center-ledger":
                    await Registers.Report(visibility, ct, "cost-center-ledger").ConfigureAwait(false);
                    return (object?)await reports.CostCenterLedgerAsync(await PlaceAsync(costCenter, ct).ConfigureAwait(false), From(), To(), ct).ConfigureAwait(false)
                        ?? throw new McpException("There is no such cost centre that you may see.");
                case "cost-center-balance":
                    return (object?)await reports.CostCenterBalanceAsync(await PlaceAsync(costCenter, ct).ConfigureAwait(false), to, ct).ConfigureAwait(false)
                        ?? throw new McpException("There is no such cost centre that you may see.");
                case "staff-ledger":
                    await Registers.Report(visibility, ct, "staff-ledger").ConfigureAwait(false);
                    var employeeId = await Registers.ElementAsync(s => Registers.Employees(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct),
                        employee ?? throw new McpException("Give the employee."), "employee").ConfigureAwait(false);
                    return (object?)await reports.EmployeeLedgerAsync(employeeId, From(), To(), ct).ConfigureAwait(false)
                        ?? throw new McpException("There is no such employee that you may see.");
                case "opportunity-documents":
                    await Registers.Report(visibility, ct, "opportunity-documents").ConfigureAwait(false);
                    return await opportunities.DocumentsReportAsync(from, to, stage, Registers.English, ct).ConfigureAwait(false);
                case "unbalanced-scenarios":
                    await Registers.Report(visibility, ct, "unbalanced-scenarios").ConfigureAwait(false);
                    return await posting.UnbalancedListAsync(await visibility.CompanyAsync(ct).ConfigureAwait(false), Registers.English, ct).ConfigureAwait(false);
                case "vat-return-form":
                    await Registers.Report(visibility, ct, "vat-return-form").ConfigureAwait(false);
                    return await reports.VatReturnFormAsync(From(), To(), Registers.English, ct).ConfigureAwait(false);
                default:
                    throw new McpException("Unknown report. Reports: ageing, vat-register, document-totals, contracts, item-tracking, purchase-prices, partner-ledger, partner-statistics, "
                        + "partner-ranking, cost-center-ledger, cost-center-balance, staff-ledger, opportunity-documents, unbalanced-scenarios, vat-return-form.");
            }
        });

    private async Task<Guid[]> DocumentCategoriesAsync(IReadOnlyList<string>? given, CancellationToken ct)
    {
        var known = await documents.Entities.CategoriesAsync(ct).ConfigureAwait(false);
        var ids = (given ?? []).Select(c => Registers.Category(known, c, "document")!.Value).Distinct().ToArray();
        return ids.Length > 0 ? ids : throw new McpException("Give at least one document category.");
    }

    private async Task<Guid> ProductAsync(string? given, CancellationToken ct) =>
        await Registers.ElementAsync(s => Registers.Products(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct),
            given ?? throw new McpException("Give the product."), "product").ConfigureAwait(false);

    private async Task<Guid> PartnerAsync(string? given, CancellationToken ct) =>
        await Registers.ElementAsync(s => Registers.Partners(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct),
            given ?? throw new McpException("Give the partner."), "partner").ConfigureAwait(false);

    private async Task<Guid> PlaceAsync(string? given, CancellationToken ct) =>
        await Registers.ElementAsync(s => Registers.CostCenters(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct),
            given ?? throw new McpException("Give the cost centre."), "cost centre").ConfigureAwait(false);
}

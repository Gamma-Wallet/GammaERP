using System.ComponentModel;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Opportunities;
using Gamma.Erp.Services.Reports;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>The reports through Claude: the same report services, each behind the same report of the menu as its route (RequireReport).</summary>
[McpServerToolType]
public sealed class ReportTools(ReportService reports, StockReportService stock, OpenItems openItems, DashboardService dashboard, OpportunityService opportunities,
    DocumentService documents, ErpDbContext db, RequestContext request, Visibility visibility)
{
    private const string CostCenterHelp = "Optional: one cost centre (id or exact code).";

    [McpServerTool(Name = "trial_balance", Title = "Trial balance", ReadOnly = true)]
    [Description("The trial balance for a period: per account its opening, the period's debit and credit, and the closing balance, with the totals. Rows that are all zero are left out. Use maxLevel to see only the upper levels of the chart.")]
    public Task<object> TrialBalanceAsync([Description("From (yyyy-mm-dd).")] DateOnly from, [Description("To (yyyy-mm-dd).")] DateOnly to,
        [Description("Optional: the chart (code, name or id); the statutory chart when left out.")] string? chart = null,
        [Description(CostCenterHelp)] string? costCenter = null,
        [Description("Optional: only accounts up to this level of the tree (1 = top).")] int? maxLevel = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Report(visibility, ct, "trial-balance").ConfigureAwait(false);
            var tb = await reports.TrialBalanceAsync(from, to, await ChartAsync(chart, ct).ConfigureAwait(false), Registers.English, await CostCenterAsync(costCenter, ct).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            return new
            {
                tb.Chart, tb.From, tb.To, tb.TotalDebit, tb.TotalCredit,
                rows = tb.Rows.Where(r => (maxLevel is null || r.Level <= maxLevel) && (r.OpeningDebit != 0 || r.OpeningCredit != 0 || r.Debit != 0 || r.Credit != 0 || r.ClosingDebit != 0 || r.ClosingCredit != 0))
                    .Select(r => new { r.AccountId, r.Code, r.Name, r.Level, group = r.IsGroup, r.OpeningDebit, r.OpeningCredit, r.Debit, r.Credit, r.ClosingDebit, r.ClosingCredit }),
            };
        });

    [McpServerTool(Name = "balance_sheet", Title = "Balance sheet", ReadOnly = true)]
    [Description("The balance sheet on a day, by its configured lines (with the year before), the assets, liabilities and equity totals, the difference, and any accounts not placed on a line.")]
    public Task<object> BalanceSheetAsync([Description("The day (yyyy-mm-dd); today when left out.")] DateOnly? to = null,
        [Description("Optional: the chart (code, name or id).")] string? chart = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Report(visibility, ct, "balance-sheet").ConfigureAwait(false);
            var b = await reports.BalanceSheetAsync(to ?? DateOnly.FromDateTime(DateTime.Today), await ChartAsync(chart, ct).ConfigureAwait(false), Registers.English, ct).ConfigureAwait(false);
            return new { b.To, b.Assets, b.LiabilitiesAndEquity, b.Difference, rows = Statement(b.Rows), unplaced = b.Unplaced };
        });

    [McpServerTool(Name = "profit_and_loss", Title = "Profit and loss", ReadOnly = true)]
    [Description("The profit and loss statement for a period, by its configured lines, with the same period a year before and the result.")]
    public Task<object> ProfitAndLossAsync([Description("From (yyyy-mm-dd).")] DateOnly from, [Description("To (yyyy-mm-dd).")] DateOnly to,
        [Description("Optional: the chart (code, name or id).")] string? chart = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Report(visibility, ct, "profit-loss").ConfigureAwait(false);
            var p = await reports.ProfitLossAsync(from, to, await ChartAsync(chart, ct).ConfigureAwait(false), Registers.English, ct).ConfigureAwait(false);
            return new { p.From, p.To, p.Result, p.PreviousResult, rows = Statement(p.Rows), unplaced = p.Unplaced };
        });

    [McpServerTool(Name = "account_ledger", Title = "Account ledger", ReadOnly = true)]
    [Description("One account's ledger for a period: the opening balance, each entry (date, document, from, to, debit, credit, running balance) and the closing balance. Long ledgers come a page at a time.")]
    public Task<object> AccountLedgerAsync([Description("The account: id or exact code (find_accounts).")] string account,
        [Description("From (yyyy-mm-dd).")] DateOnly from, [Description("To (yyyy-mm-dd).")] DateOnly to, [Description(CostCenterHelp)] string? costCenter = null,
        [Description("Page number, from 1.")] int? page = null, [Description("Entries per page: 25 if not given, at most 100.")] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Report(visibility, ct, "ledger").ConfigureAwait(false);
            var id = await Registers.ElementAsync(s => Registers.Charts(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), account, "account")
                .ConfigureAwait(false);
            var l = await reports.LedgerAsync(id, from, to, Registers.English, await CostCenterAsync(costCenter, ct).ConfigureAwait(false), ct).ConfigureAwait(false)
                ?? throw new McpException("There is no such account that you may see.");
            var (p, size) = Registers.Paging(page, pageSize);
            return new
            {
                l.AccountId, l.Code, l.Name, l.From, l.To, l.Opening, l.Closing, entries = l.Lines.Count, page = p, pageSize = size,
                lines = l.Lines.Skip((p - 1) * size).Take(size).Select(x => new { x.PostingDate, x.DocumentId, x.Document, x.From, x.To, x.Debit, x.Credit, x.Balance }),
            };
        });

    [McpServerTool(Name = "open_items", Title = "Open invoices", ReadOnly = true)]
    [Description("The invoices still open on a day (receivable from clients and payable to suppliers): number, partner, date, due date, total, open amount, days overdue, with the open totals. Filter by one partner.")]
    public Task<object> OpenItemsAsync([Description("The day (yyyy-mm-dd); today when left out.")] DateOnly? to = null,
        [Description("Optional: one partner (id or exact code).")] string? partner = null,
        [Description("Page number, from 1.")] int? page = null, [Description("Rows per page: 25 if not given, at most 100.")] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Report(visibility, ct, "open-items").ConfigureAwait(false);
            Guid? partnerId = string.IsNullOrWhiteSpace(partner) ? null
                : await Registers.ElementAsync(s => Registers.Partners(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), partner, "partner")
                    .ConfigureAwait(false);
            var rows = await openItems.ListAsync(to ?? DateOnly.FromDateTime(DateTime.Today), partnerId, ct).ConfigureAwait(false);
            var (p, size) = Registers.Paging(page, pageSize);
            return new
            {
                count = rows.Count, receivableOpen = rows.Where(r => r.Receivable).Sum(r => r.Open), payableOpen = rows.Where(r => !r.Receivable).Sum(r => r.Open), page = p, pageSize = size,
                items = rows.OrderByDescending(r => r.DaysOverdue).Skip((p - 1) * size).Take(size).Select(r => new
                {
                    r.Id, number = r.Code, r.Partner, side = r.Receivable ? "receivable" : "payable", r.Date, r.DueDate, r.Total, r.Open, r.DaysOverdue,
                }),
            };
        });

    [McpServerTool(Name = "stock", Title = "Stock held", ReadOnly = true)]
    [Description("What is held now per place and product: quantity, unit, unit cost and value, with the total value. Filter by a place (warehouse, shop) or a product category.")]
    public Task<object> StockAsync([Description(CostCenterHelp)] string? costCenter = null,
        [Description("Optional: one product category (code, name or id).")] string? productCategory = null,
        [Description("Page number, from 1.")] int? page = null, [Description("Rows per page: 25 if not given, at most 100.")] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Report(visibility, ct, "stock").ConfigureAwait(false);
            var category = Registers.Category(await Registers.Products(db, request, visibility).CategoriesAsync(ct).ConfigureAwait(false), productCategory, "product");
            var rows = await stock.StockAsync(await CostCenterAsync(costCenter, ct).ConfigureAwait(false), category, StockReportService.PlaceHolder, Registers.English, ct).ConfigureAwait(false);
            var (p, size) = Registers.Paging(page, pageSize);
            return new
            {
                count = rows.Count, value = rows.Sum(r => r.Value), page = p, pageSize = size,
                rows = rows.Skip((p - 1) * size).Take(size).Select(r => new { r.CostCenter, r.ProductId, r.Code, r.Product, r.Unit, r.Quantity, r.UnitCost, r.Value }),
            };
        });

    [McpServerTool(Name = "sales_lines", Title = "Sales and purchase lines", ReadOnly = true)]
    [Description("The lines report: the posted lines of the given document categories (e.g. sales invoices) in a period, with totals and the sums by cost centre, by partner and by user (waiter). Reversals count negative. Filter by a cost centre or a partner.")]
    public Task<object> SalesLinesAsync([Description("The document categories (codes, names or ids), e.g. the sales invoices' category.")] IReadOnlyList<string> categories,
        [Description("From (yyyy-mm-dd).")] DateOnly from, [Description("To (yyyy-mm-dd).")] DateOnly to, [Description(CostCenterHelp)] string? costCenter = null,
        [Description("Optional: one partner (id or exact code).")] string? partner = null,
        [Description("Page number of the lines, from 1.")] int? page = null, [Description("Lines per page: 25 if not given, at most 100.")] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Report(visibility, ct, "lines", "lines-by-partner", "lines-by-cost-center", "lines-by-user", "transfers").ConfigureAwait(false);
            var known = await documents.Entities.CategoriesAsync(ct).ConfigureAwait(false);
            var ids = (categories ?? []).Select(c => Registers.Category(known, c, "document")!.Value).Distinct().ToList();
            if (ids.Count == 0)
            {
                throw new McpException("Give at least one document category.");
            }

            Guid? partnerId = string.IsNullOrWhiteSpace(partner) ? null
                : await Registers.ElementAsync(s => Registers.Partners(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), partner, "partner")
                    .ConfigureAwait(false);
            var l = await stock.LinesAsync(ids, from, to, await CostCenterAsync(costCenter, ct).ConfigureAwait(false), partnerId, Registers.English, ct).ConfigureAwait(false);
            var (p, size) = Registers.Paging(page, pageSize);
            return new
            {
                l.Value, l.Vat, l.Total, count = l.Lines.Count, l.ByCostCenter, l.ByPartner, l.ByUser, page = p, pageSize = size,
                lines = l.Lines.Skip((p - 1) * size).Take(size).Select(x => new { x.PostingDate, x.DocumentId, x.Document, x.Partner, x.CostCenter, x.Item, x.Quantity, x.Unit, x.Value, x.Vat, x.Total, x.User }),
            };
        });

    [McpServerTool(Name = "vat_return", Title = "VAT return", ReadOnly = true)]
    [Description("The VAT return for a period. Without categories: the company's configured return form (its boxes, output VAT, input VAT, VAT owed). With output and input document categories: the base and VAT per VAT group on each side and the VAT owed.")]
    public Task<object> VatReturnAsync([Description("From (yyyy-mm-dd).")] DateOnly from, [Description("To (yyyy-mm-dd).")] DateOnly to,
        [Description("Optional: the output (sales) document categories.")] IReadOnlyList<string>? output = null,
        [Description("Optional: the input (purchase) document categories.")] IReadOnlyList<string>? input = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            if ((output?.Count ?? 0) == 0 && (input?.Count ?? 0) == 0)
            {
                await Registers.Report(visibility, ct, "vat-return-form").ConfigureAwait(false);
                return await reports.VatReturnFormAsync(from, to, Registers.English, ct).ConfigureAwait(false);
            }

            await Registers.Report(visibility, ct, "vat-return").ConfigureAwait(false);
            var known = await documents.Entities.CategoriesAsync(ct).ConfigureAwait(false);
            return await reports.VatReturnAsync([.. (output ?? []).Select(c => Registers.Category(known, c, "document")!.Value)],
                [.. (input ?? []).Select(c => Registers.Category(known, c, "document")!.Value)], from, to, Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "forecast", Title = "Opportunity forecast", ReadOnly = true)]
    [Description("The sales forecast from the opportunities: per expected-close month and currency, the open amount, the amount weighted by the stage's chance, what was won and what was invoiced. Currencies are never added together.")]
    public Task<object> ForecastAsync(CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Report(visibility, ct, "opportunity-forecast").ConfigureAwait(false);
            return await opportunities.ForecastAsync(Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "dashboard", Title = "Dashboard figures", ReadOnly = true)]
    [Description("The home dashboard's figures for a period, as the company configured them: each card's value and the previous period's, each chart's or list's rows (at most 12). Amounts are posted, net of VAT, in LCU. Start here for \"how are we doing?\".")]
    public Task<object> DashboardAsync([Description("From (yyyy-mm-dd).")] DateOnly from, [Description("To (yyyy-mm-dd).")] DateOnly to,
        [Description(CostCenterHelp)] string? costCenter = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var d = await dashboard.DashboardAsync(from, to, await CostCenterAsync(costCenter, ct).ConfigureAwait(false), Registers.English, ct).ConfigureAwait(false);
            return new
            {
                d.From, d.To, d.PreviousFrom, d.PreviousTo, d.Currency,
                items = d.Items.Select(i => new
                {
                    i.Code, i.Name, i.Kind, i.Value, i.Previous, i.FirstName, i.SecondName,
                    rows = i.Rows.Count == 0 ? null : i.Rows.Take(12).Select(r => new { r.Name, r.Value, r.Second }),
                }),
            };
        });

    private static IEnumerable<object> Statement(IReadOnlyList<StatementRowDto> rows) =>
        rows.Where(r => r.IsHeading || r.Amount != 0 || r.Previous != 0).Select(r => new { r.Code, r.Name, r.Level, heading = r.IsHeading, r.Amount, r.Previous });

    private async Task<Guid?> CostCenterAsync(string? given, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(given) ? null
            : await Registers.ElementAsync(s => Registers.CostCenters(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), given, "cost centre")
                .ConfigureAwait(false);

    private async Task<Guid?> ChartAsync(string? given, CancellationToken ct) =>
        Registers.Category(await Registers.Charts(db, request, visibility).CategoriesAsync(ct).ConfigureAwait(false), given, "chart");
}

using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Services.Posting;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>
/// What a document list asks for: the category (required) and
/// optionally its type, a search, a filter per column, the user who wrote the documents and a date-time range (a point of sale's sales per
/// waiter), the sort column and direction, the page.
/// </summary>
public sealed record DocumentQuery(Guid Category, Guid? Type = null, string? Search = null, string? Code = null, string? From = null, string? To = null,
    string? ExternalNumber = null, bool? Posted = null, DateOnly? PostedFrom = null, DateOnly? PostedTo = null, DateOnly? DatedFrom = null, DateOnly? DatedTo = null,
    Guid? CreatedBy = null, DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, decimal? TotalFrom = null, decimal? TotalTo = null,
    string? Sort = null, bool Descending = false, int Page = 1, int PageSize = 50)
{
    /// <summary>The columns a list sorts by.</summary>
    public static readonly IReadOnlySet<string> SortColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "code", "postingDate", "documentDate", "from", "to", "externalNumber", "total", "createdOn",
    };
}

/// <summary>A row of a document list: the document's columns and its totals in the company's currency.</summary>
public sealed record DocumentListItemDto(Guid Id, string? Code, Guid TypeId, string? ExternalNumber, Guid? FromId, string? FromName, Guid? ToId, string? ToName,
    bool Posted, DateOnly? PostingDate, DateOnly? DocumentDate, Guid? CurrencyId, decimal? ExchangeRate, decimal Discount, decimal Value, decimal Vat, decimal Total,
    Guid? CreatedBy, DateTimeOffset CreatedOn, string? ReversedDocumentCode, string? CurrencyCode = null);

/// <summary>The documents before and after one, of its type, in the order they were made.</summary>
public sealed record DocumentNeighboursDto(Guid? Previous, Guid? Next);

/// <summary>An entry posting would write: the line it comes from, the account, the cost centre, debit and credit.</summary>
public sealed record PostingPreviewRowDto(string? Line, Guid ChartId, string? Account, string? AccountName, Guid? CostCenterId, string? CostCenter, decimal Debit,
    decimal Credit);

public sealed partial class DocumentService
{
    /// <summary>A page of the documents of a category the request sees, filtered per column and sorted (by default the newest first).</summary>
    public async Task<PageDto<DocumentListItemDto>> ListAsync(DocumentQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, size) = (Math.Max(query.Page, 1), Math.Clamp(query.PageSize, 1, 500));
        if (query.Sort is { } sort && !DocumentQuery.SortColumns.Contains(sort))
        {
            throw ValidationException.For("sort", $"A list is sorted by one of: {string.Join(", ", DocumentQuery.SortColumns)}.");
        }

        if (await Entities.VisibleInCategoryAsync(query.Category, query.Search, ct).ConfigureAwait(false) is not { } visible)
        {
            return new PageDto<DocumentListItemDto>([], 0, page, size);
        }

        var documents = visible.Where(d => d.Active);
        if (query.Type is { } type) documents = documents.Where(d => d.TypeId == type);
        if (Pattern(query.Code) is { } code) documents = documents.Where(d => d.Code != null && EF.Functions.ILike(d.Code, code));
        if (Pattern(query.From) is { } from) documents = documents.Where(d => d.FromName != null && EF.Functions.ILike(d.FromName, from));
        if (Pattern(query.To) is { } to) documents = documents.Where(d => d.ToName != null && EF.Functions.ILike(d.ToName, to));
        if (Pattern(query.ExternalNumber) is { } external) documents = documents.Where(d => d.ExternalNumber != null && EF.Functions.ILike(d.ExternalNumber, external));
        if (query.Posted is { } posted) documents = documents.Where(d => d.Posted == posted);
        if (query.PostedFrom is { } pf) documents = documents.Where(d => d.PostingDate >= pf);
        if (query.PostedTo is { } pt) documents = documents.Where(d => d.PostingDate <= pt);
        if (query.DatedFrom is { } df) documents = documents.Where(d => d.DocumentDate >= df);
        if (query.DatedTo is { } dt) documents = documents.Where(d => d.DocumentDate <= dt);
        if (query.CreatedBy is { } user) documents = documents.Where(d => d.CreatedBy == user);
        if (query.CreatedFrom is { } cf) documents = documents.Where(d => d.CreatedOn >= cf);
        if (query.CreatedTo is { } cto) documents = documents.Where(d => d.CreatedOn <= cto);

        var totals = db.Set<DocumentTotal>().Where(t => t.Active);
        var rows = documents.Select(d => new
        {
            Document = d,
            Discount = totals.Where(t => t.DocumentId == d.Id).Sum(t => t.DiscountLcu ?? 0),
            Value = totals.Where(t => t.DocumentId == d.Id).Sum(t => t.ValueLcu ?? 0),
            Vat = totals.Where(t => t.DocumentId == d.Id).Sum(t => t.VatLcu ?? 0),
            Total = totals.Where(t => t.DocumentId == d.Id).Sum(t => t.ValueVatLcu ?? 0),
        });
        if (query.TotalFrom is { } min) rows = rows.Where(r => r.Total >= min);
        if (query.TotalTo is { } max) rows = rows.Where(r => r.Total <= max);

        var ordered = (query.Sort?.ToLowerInvariant(), query.Descending) switch
        {
            // A code follows the type's counter, so the order the documents were made in is their codes' order (FSHM2 before FSHM10).
            ("code", false) => rows.OrderBy(r => r.Document.Id),
            ("code", true) => rows.OrderByDescending(r => r.Document.Id),
            ("postingdate", false) => rows.OrderBy(r => r.Document.PostingDate).ThenBy(r => r.Document.Id),
            ("postingdate", true) => rows.OrderByDescending(r => r.Document.PostingDate).ThenByDescending(r => r.Document.Id),
            ("documentdate", false) => rows.OrderBy(r => r.Document.DocumentDate).ThenBy(r => r.Document.Id),
            ("documentdate", true) => rows.OrderByDescending(r => r.Document.DocumentDate).ThenByDescending(r => r.Document.Id),
            ("from", false) => rows.OrderBy(r => r.Document.FromName).ThenBy(r => r.Document.Id),
            ("from", true) => rows.OrderByDescending(r => r.Document.FromName).ThenByDescending(r => r.Document.Id),
            ("to", false) => rows.OrderBy(r => r.Document.ToName).ThenBy(r => r.Document.Id),
            ("to", true) => rows.OrderByDescending(r => r.Document.ToName).ThenByDescending(r => r.Document.Id),
            ("externalnumber", false) => rows.OrderBy(r => r.Document.ExternalNumber).ThenBy(r => r.Document.Id),
            ("externalnumber", true) => rows.OrderByDescending(r => r.Document.ExternalNumber).ThenByDescending(r => r.Document.Id),
            ("total", false) => rows.OrderBy(r => r.Total).ThenBy(r => r.Document.Id),
            ("total", true) => rows.OrderByDescending(r => r.Total).ThenByDescending(r => r.Document.Id),
            ("createdon", false) => rows.OrderBy(r => r.Document.CreatedOn).ThenBy(r => r.Document.Id),
            ("createdon", true) => rows.OrderByDescending(r => r.Document.CreatedOn).ThenByDescending(r => r.Document.Id),
            _ => rows.OrderByDescending(r => r.Document.Id),
        };
        var total = await rows.CountAsync(ct).ConfigureAwait(false);
        var pageRows = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct).ConfigureAwait(false);

        // The currency and rate of each document on the page: its first total's (a document is usually in one currency).
        var ids = pageRows.Select(r => (Guid?)r.Document.Id).ToList();
        var currencies = (await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.Active && ids.Contains(t.DocumentId)).OrderBy(t => t.Id)
            .Select(t => new { t.DocumentId, t.CurrencyId, t.ExchangeRate }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.DocumentId!.Value).ToDictionary(g => g.Key, g => g.First());
        var currencyIds = currencies.Values.Select(c => c.CurrencyId).ToList();
        var codes = await db.Set<Gamma.Erp.Models.Currencies.Currency>().AsNoTracking().Where(c => currencyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        return new PageDto<DocumentListItemDto>([.. pageRows.Select(r =>
        {
            var currency = currencies.GetValueOrDefault(r.Document.Id);
            return new DocumentListItemDto(r.Document.Id, r.Document.Code, r.Document.TypeId, r.Document.ExternalNumber, r.Document.FromId, r.Document.FromName,
                r.Document.ToId, r.Document.ToName, r.Document.Posted, r.Document.PostingDate, r.Document.DocumentDate, currency?.CurrencyId, currency?.ExchangeRate,
                r.Discount, r.Value, r.Vat, r.Total, r.Document.CreatedBy, r.Document.CreatedOn, r.Document.ReversedDocumentCode,
                currency?.CurrencyId is { } id ? codes.GetValueOrDefault(id) : null);
        })], total, page, size);
    }

    /// <summary>The documents of the same type made just before and after this one that the request sees; null when it does not see this one.</summary>
    public async Task<DocumentNeighboursDto?> NeighboursAsync(Guid id, CancellationToken ct)
    {
        if (await DocumentAsync(id, ct).ConfigureAwait(false) is not { } document || document.CategoryIds.Count == 0)
        {
            return null;
        }

        var visible = await Entities.VisibleInCategoryAsync(document.CategoryIds[0], null, ct).ConfigureAwait(false);
        if (visible is null)
        {
            return new DocumentNeighboursDto(null, null);
        }

        var sameType = visible.Where(d => d.Active && d.TypeId == document.TypeId);
        var previous = await sameType.Where(d => d.Id.CompareTo(id) < 0).OrderByDescending(d => d.Id).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var next = await sameType.Where(d => d.Id.CompareTo(id) > 0).OrderBy(d => d.Id).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new DocumentNeighboursDto(previous, next);
    }

    /// <summary>
    /// The entries posting the document would write now, worked out and checked as posting does but nothing written;
    /// null when the request does not see it. Refused with the same errors posting would give.
    /// </summary>
    public async Task<IReadOnlyList<PostingPreviewRowDto>?> PostingPreviewAsync(Guid id, CancellationToken ct)
    {
        if (await DocumentAsync(id, ct).ConfigureAwait(false) is null)
        {
            return null;
        }

        var document = await db.Set<Document>().AsNoTracking().FirstAsync(d => d.Id == id, ct).ConfigureAwait(false);
        var lines = (await db.Set<Transaction>().AsNoTracking().Where(t => t.DocumentId == id && t.Active).ToListAsync(ct).ConfigureAwait(false)).OrderBy(Line).ToList();
        if (document.Posted)
        {
            // What posting wrote: the document's entries and, for a sheet, its journals'.
            var ids = await db.Set<Document>().AsNoTracking().Where(d => d.Id == id || d.IdSup == id).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
            var written = await db.Set<ChartValue>().AsNoTracking().Where(v => v.Active && v.ChartId != null && v.DocumentId != null && ids.Contains(v.DocumentId.Value))
                .OrderBy(v => v.Id).ToListAsync(ct).ConfigureAwait(false);
            var all = await db.Set<Transaction>().AsNoTracking().Where(t => t.DocumentId != null && ids.Contains(t.DocumentId.Value)).ToListAsync(ct).ConfigureAwait(false);
            return await RowsAsync([.. written.Select(v => new PostingEntry(v.ChartId!.Value, v.CostCenterId, v.TransactionId ?? Guid.Empty, v.Debit ?? 0, v.Credit ?? 0))], all, ct)
                .ConfigureAwait(false);
        }

        // The stock costs first (worked out, not written): the entries can read them.
        await new MovementService(db).PlanAsync(document, lines, ct).ConfigureAwait(false);
        await new Depreciation.AssetSale(db).FillAsync(document, lines, ct).ConfigureAwait(false);
        var entries = (await new PostingService(db).EntriesAsync(document, lines, ct).ConfigureAwait(false)).ToList();
        // A sheet's entries are its journals'.
        foreach (var journal in await db.Set<Document>().AsNoTracking().Where(d => d.IdSup == id).OrderBy(d => d.Id).ToListAsync(ct).ConfigureAwait(false))
        {
            var rows = (await db.Set<Transaction>().AsNoTracking().Where(t => t.DocumentId == journal.Id).ToListAsync(ct).ConfigureAwait(false)).OrderBy(Line).ToList();
            entries.AddRange(await new PostingService(db).EntriesAsync(journal, rows, ct, balanced: false).ConfigureAwait(false));
            lines.AddRange(rows);
        }

        return await RowsAsync(entries, lines, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PostingPreviewRowDto>> RowsAsync(IReadOnlyList<PostingEntry> entries, IReadOnlyList<Transaction> lines, CancellationToken ct)
    {
        var chartIds = entries.Select(e => e.ChartId).Distinct().ToList();
        var accounts = await db.Set<Chart>().AsNoTracking().Where(c => chartIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var centreIds = entries.Where(e => e.CostCenterId != null).Select(e => e.CostCenterId!.Value).Distinct().ToList();
        var centres = await db.Set<CostCenter>().AsNoTracking().Where(c => centreIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var codes = lines.ToDictionary(l => l.Id, l => l.Code);
        return [.. entries.Select(e => new PostingPreviewRowDto(codes.GetValueOrDefault(e.TransactionId), e.ChartId, accounts.GetValueOrDefault(e.ChartId)?.Code,
            accounts.GetValueOrDefault(e.ChartId)?.Name.In(Models.Base.TranslatedText.DefaultLanguage), e.CostCenterId,
            e.CostCenterId is { } c ? centres.GetValueOrDefault(c)?.Name.In(Models.Base.TranslatedText.DefaultLanguage) : null, e.Debit, e.Credit))];
    }

    /// <summary>
    /// A document done again for another day: a copy of the document, of the documents under it (a sheet's
    /// journals) and of their lines, dated that day and linked to what they repeat; posted when asked, the sheet with its journals. Every
    /// rule of a new document applies (a reversal is not repeated, a type that never posts is not posted). Null when the request does not see it.
    /// </summary>
    public async Task<DocumentDto?> RepeatAsync(Guid id, DateOnly date, bool post, CancellationToken ct)
    {
        if (await DocumentAsync(id, ct).ConfigureAwait(false) is not { } original)
        {
            return null;
        }

        var children = await db.Set<Document>().AsNoTracking().Where(d => d.IdSup == id && d.Active).OrderBy(d => d.Id).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var copy = await SaveAsync(Again(original, date, null), ct).ConfigureAwait(false);
        foreach (var child in children)
        {
            if (await DocumentAsync(child, ct).ConfigureAwait(false) is { } journal)
            {
                await SaveAsync(Again(journal, date, copy.Id), ct).ConfigureAwait(false);
            }
        }

        if (post)
        {
            copy = await PostAsync(copy.Id!.Value, ct).ConfigureAwait(false);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return await DocumentAsync(copy.Id!.Value, ct).ConfigureAwait(false);
    }

    private static DocumentDto Again(DocumentDto document, DateOnly date, Guid? under) => document with
    {
        Id = null, Code = null, IdSup = under, Posted = false, DocumentDate = date, PostingDate = date, CreatedFromIds = [document.Id!.Value], Totals = [],
        ReversedDocumentId = null, ReversedDocumentCode = null, ExternalNumber = null,
        Transactions = [.. document.Transactions.Select(t => t with { Id = null, IdSup = null, Code = null, DocumentId = null, Posted = false })],
    };

    // A column filter matches its text anywhere ("contains"), case aside.
    private static string? Pattern(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}

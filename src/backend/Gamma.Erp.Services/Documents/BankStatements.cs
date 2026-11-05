using System.Globalization;
using System.Text;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

public sealed record BankOptionDto(Guid Id, string? Name);

public sealed record BankStatementDto(Guid Id, Guid BankId, string? Bank, string? FileName, DateTimeOffset ImportedOn, int Lines, int Open);

public sealed record BankStatementLineDto(Guid Id, DateOnly Date, string? Text, string? Reference, decimal Amount, Guid? DocumentId, Guid? DocumentCategoryId, string? DocumentCode, bool DocumentReversed, bool Ignored);

public sealed record BankImportDto(Guid? Id, int Added, int Skipped);

public sealed record BankMatchDto(Guid Id, string? Code, string? Partner, DateOnly? Date, decimal Amount);

/// <summary>A document type a person can write for a line, with the side the bank goes on.</summary>
public sealed record BankNewDocumentDto(Guid TypeId, Guid CategoryId, string? Name, bool BankIsFrom);

public sealed record BankLineChoicesDto(Guid BankId, string? Bank, IReadOnlyList<BankMatchDto> Matches, IReadOnlyList<BankNewDocumentDto> Types);

/// <summary>
/// Bank statements (the founder, 2026-11-02): a file is uploaded by hand, read by the format the person chose, and a person decides every line: match it to a posted document of
/// that bank with the same amount, write a new document, or ignore it. Nothing is matched automatically and a statement posts nothing. Uploading a file again adds only lines not
/// yet held.
/// </summary>
public sealed class BankStatements(ErpDbContext db, RequestContext request, Visibility visibility)
{
    private const int MaxMatches = 50;

    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>The bank accounts the request sees.</summary>
    public async Task<IReadOnlyList<BankOptionDto>> BanksAsync(string language, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var types = db.Set<CostCenterType>().Where(t => t.TakesBankStatements).Select(t => t.Id);
        var banks = await db.Set<CostCenter>().AsNoTracking().Where(c => c.Active && types.Contains(c.TypeId) && nodes.Contains(c.Id)).ToListAsync(ct).ConfigureAwait(false);
        return [.. banks.Select(b => new BankOptionDto(b.Id, b.Name.In(language) ?? b.Code)).OrderBy(b => b.Name)];
    }

    /// <summary>The file formats of the request's company.</summary>
    public async Task<IReadOnlyList<BankOptionDto>> FormatsAsync(string language, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var formats = await db.Set<BankStatementFormat>().AsNoTracking().Where(f => f.Active && companies.Contains(f.OwnerId!.Value)).ToListAsync(ct).ConfigureAwait(false);
        return [.. formats.Select(f => new BankOptionDto(f.Id, f.Name.In(language))).OrderBy(f => f.Name)];
    }

    /// <summary>The statements, newest first, with how many lines are still undecided.</summary>
    public async Task<IReadOnlyList<BankStatementDto>> ListAsync(string language, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var statements = await db.Set<BankStatement>().AsNoTracking().Where(s => s.Active && companies.Contains(s.OwnerId!.Value)).OrderByDescending(s => s.CreatedOn).ToListAsync(ct).ConfigureAwait(false);
        var ids = statements.Select(s => s.Id).ToList();
        var counts = await db.Set<BankStatementLine>().AsNoTracking().Where(l => l.Active && ids.Contains(l.StatementId)).GroupBy(l => l.StatementId)
            .Select(g => new { Id = g.Key, Lines = g.Count(), Open = g.Count(l => l.DocumentId == null && !l.Ignored) }).ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var bankIds = statements.Select(s => s.BankId).Distinct().ToList();
        var banks = await db.Set<CostCenter>().AsNoTracking().Where(c => bankIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(language) ?? c.Code, ct).ConfigureAwait(false);
        return [.. statements.Select(s => new BankStatementDto(s.Id, s.BankId, banks.GetValueOrDefault(s.BankId), s.FileName, s.CreatedOn, counts.GetValueOrDefault(s.Id)?.Lines ?? 0, counts.GetValueOrDefault(s.Id)?.Open ?? 0))];
    }

    /// <summary>Reads a file by the format chosen, all or nothing, and keeps the lines not yet held for that bank.</summary>
    public async Task<BankImportDto> ImportAsync(Stream content, string fileName, Guid bankId, Guid formatId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("ownerId", "Bank statements are uploaded in a company or one of its cost centres, not at the corporate.");
        var banks = await BanksAsync("en", ct).ConfigureAwait(false);
        if (banks.All(b => b.Id != bankId))
        {
            throw ValidationException.For("bankId", "The bank does not exist.");
        }

        var format = await db.Set<BankStatementFormat>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == formatId && f.Active && f.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("formatId", "The file format does not exist.");
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var read = Read(await reader.ReadToEndAsync(ct).ConfigureAwait(false), format);
        var statements = db.Set<BankStatement>().Where(s => s.Active && s.BankId == bankId).Select(s => s.Id);
        var held = (await db.Set<BankStatementLine>().AsNoTracking().Where(l => l.Active && statements.Contains(l.StatementId)).Select(l => new { l.LineDate, l.Amount, l.Text, l.Reference }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => (l.LineDate, l.Amount, l.Text, l.Reference)).ToDictionary(g => g.Key, g => g.Count());

        // Two equal lines in one file are two payments: only as many as are already held count as held.
        var fresh = new List<BankStatementLine>();
        foreach (var line in read)
        {
            var key = (line.LineDate, line.Amount, line.Text, line.Reference);
            if (held.TryGetValue(key, out var left) && left > 0)
            {
                held[key] = left - 1;
            }
            else
            {
                fresh.Add(line);
            }
        }

        if (fresh.Count == 0)
        {
            return new BankImportDto(null, 0, read.Count);
        }

        var statement = new BankStatement { BankId = bankId, FormatId = formatId, FileName = fileName, TenantId = request.TenantId, OwnerId = company };
        db.Add(statement);
        foreach (var line in fresh)
        {
            (line.StatementId, line.TenantId, line.OwnerId) = (statement.Id, request.TenantId, company);
            db.Add(line);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new BankImportDto(statement.Id, fresh.Count, read.Count - fresh.Count);
    }

    /// <summary>A statement's lines, in date order.</summary>
    public async Task<IReadOnlyList<BankStatementLineDto>> LinesAsync(Guid id, CancellationToken ct)
    {
        await StatementAsync(id, ct).ConfigureAwait(false);
        var lines = await db.Set<BankStatementLine>().AsNoTracking().Where(l => l.Active && l.StatementId == id).OrderBy(l => l.LineDate).ThenBy(l => l.Id).ToListAsync(ct).ConfigureAwait(false);
        var ids = lines.Select(l => l.DocumentId).OfType<Guid>().ToList();
        var documents = await db.Set<Document>().AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct).ConfigureAwait(false);
        var nullable = ids.Select(i => (Guid?)i).ToList();
        var reversed = (await db.Set<Document>().AsNoTracking().Where(r => r.Active && nullable.Contains(r.ReversedDocumentId)).Select(r => r.ReversedDocumentId!.Value).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var types = documents.Values.Select(d => d.TypeId).Distinct().ToList();
        var categories = (await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.Active && types.Contains(l.TypeId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.TypeId).ToDictionary(g => g.Key, g => g.First().CategoryId);
        return [.. lines.Select(l =>
        {
            var document = l.DocumentId is { } d ? documents.GetValueOrDefault(d) : null;
            return new BankStatementLineDto(l.Id, l.LineDate, l.Text, l.Reference, l.Amount, l.DocumentId, document is null ? null : categories.GetValueOrDefault(document.TypeId), document?.Code,
                document is not null && reversed.Contains(document.Id), l.Ignored);
        })];
    }

    /// <summary>What a person can do with a line: the posted documents of its bank with the same amount, nearest date first, and the types they can write instead.</summary>
    public async Task<BankLineChoicesDto> ChoicesAsync(Guid lineId, string language, CancellationToken ct)
    {
        var line = await LineAsync(lineId, ct).ConfigureAwait(false);
        var statement = await StatementAsync(line.StatementId, ct).ConfigureAwait(false);
        var bank = await db.Set<CostCenter>().AsNoTracking().Where(c => c.Id == statement.BankId).Select(c => new { c.Name, c.Code }).FirstAsync(ct).ConfigureAwait(false);
        var candidates = await CandidatesAsync(statement, line, ct).ConfigureAwait(false);
        var amount = Math.Abs(line.Amount);
        var matches = candidates.Where(c => Covers(c.Totals, amount)).OrderBy(c => Math.Abs((c.Document.DocumentDate ?? c.Document.PostingDate ?? line.LineDate).DayNumber - line.LineDate.DayNumber)).Take(MaxMatches)
            .Select(c => new BankMatchDto(c.Document.Id, c.Document.Code, line.Amount > 0 ? c.Document.FromName : c.Document.ToName, c.Document.DocumentDate ?? c.Document.PostingDate, amount)).ToList();
        return new BankLineChoicesDto(statement.BankId, bank.Name.In(language) ?? bank.Code, matches, await TypesAsync(statement.OwnerId, line.Amount < 0, language, ct).ConfigureAwait(false));
    }

    /// <summary>Matches a line to a posted document of its bank; the amounts must be the same.</summary>
    public async Task MatchAsync(Guid lineId, Guid documentId, CancellationToken ct)
    {
        var line = await LineAsync(lineId, ct).ConfigureAwait(false);
        var statement = await StatementAsync(line.StatementId, ct).ConfigureAwait(false);
        if (line.DocumentId is not null || line.Ignored)
        {
            throw ValidationException.For("line", "This statement line is already decided.");
        }

        var candidate = (await CandidatesAsync(statement, line, ct).ConfigureAwait(false)).FirstOrDefault(c => c.Document.Id == documentId)
            ?? throw ValidationException.For("documentId", "This document can't be matched to this line: it must be posted, of this bank, on the right side, and not matched yet.");
        if (!Covers(candidate.Totals, Math.Abs(line.Amount)))
        {
            throw ValidationException.For("documentId", "The document's total is not the amount of this line.");
        }

        line.DocumentId = documentId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Sets a line aside by hand.</summary>
    public async Task IgnoreAsync(Guid lineId, CancellationToken ct)
    {
        var line = await LineAsync(lineId, ct).ConfigureAwait(false);
        await StatementAsync(line.StatementId, ct).ConfigureAwait(false);
        if (line.DocumentId is not null)
        {
            throw ValidationException.For("line", "This statement line is already decided.");
        }

        line.Ignored = true;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Undoes a line's match or ignore. The document stays as it is.</summary>
    public async Task ClearAsync(Guid lineId, CancellationToken ct)
    {
        var line = await LineAsync(lineId, ct).ConfigureAwait(false);
        await StatementAsync(line.StatementId, ct).ConfigureAwait(false);
        (line.DocumentId, line.Ignored) = (null, false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Takes away a statement uploaded by mistake; refused while one of its lines is matched.</summary>
    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        var statement = await StatementAsync(id, ct).ConfigureAwait(false);
        var lines = await db.Set<BankStatementLine>().Where(l => l.Active && l.StatementId == id).ToListAsync(ct).ConfigureAwait(false);
        if (lines.Any(l => l.DocumentId is not null))
        {
            throw ValidationException.For("id", "This statement has matched lines: undo them first.");
        }

        var tracked = await db.Set<BankStatement>().FirstAsync(s => s.Id == statement.Id, ct).ConfigureAwait(false);
        tracked.Active = false;
        lines.ForEach(l => l.Active = false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private sealed record Candidate(Document Document, IReadOnlyList<DocumentTotal> Totals);

    private static bool Covers(IReadOnlyList<DocumentTotal> totals, decimal amount) =>
        Math.Round(totals.Sum(t => t.ValueVatLcu ?? 0), 2) == amount || totals.GroupBy(t => t.CurrencyId).Any(g => Math.Round(g.Sum(t => t.ValueVat ?? 0), 2) == amount);

    /// <summary>The posted documents with the bank on the line's side, not reversed and not matched to a line yet.</summary>
    private async Task<List<Candidate>> CandidatesAsync(BankStatement statement, BankStatementLine line, CancellationToken ct)
    {
        var bank = (Guid?)statement.BankId;
        var side = db.Set<Document>().AsNoTracking().Where(d => d.Active && d.Posted && d.CompanyId == statement.OwnerId && d.ReversedDocumentId == null);
        // The bank on the document's side, or on one of its lines' own side.
        var lines = db.Set<Transaction>().Where(t => t.Active);
        side = line.Amount > 0
            ? side.Where(d => d.ToId == bank || lines.Any(t => t.DocumentId == d.Id && t.ToId == bank))
            : side.Where(d => d.FromId == bank || lines.Any(t => t.DocumentId == d.Id && t.FromId == bank));
        var documents = await side.Where(d => !db.Set<BankStatementLine>().Any(l => l.Active && l.DocumentId == d.Id) && !db.Set<Document>().Any(r => r.Active && r.ReversedDocumentId == d.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = documents.Select(d => (Guid?)d.Id).ToList();
        var totals = (await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.Active && ids.Contains(t.DocumentId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(t => t.DocumentId);
        return [.. documents.Select(d => new Candidate(d, [.. totals[d.Id]]))];
    }

    /// <summary>The types that pay open items with a cost centre on the bank's side.</summary>
    private async Task<IReadOnlyList<BankNewDocumentDto>> TypesAsync(Guid? company, bool bankIsFrom, string language, CancellationToken ct)
    {
        var types = await db.Set<DocumentType>().AsNoTracking().Where(t => t.Active && t.PaysOpenItems && t.OwnerId == company).ToListAsync(ct).ConfigureAwait(false);
        var ids = types.Select(t => (Guid?)t.Id).ToList();
        var costCenters = db.Set<Elements>().Where(e => e.Table == "tbl_cost_center").Select(e => (Guid?)e.Id);
        var bankSide = bankIsFrom
            ? await db.Set<DocumentTypeConfFrom>().AsNoTracking().Where(c => c.Active && ids.Contains(c.DocumentTypeId) && costCenters.Contains(c.ElementsId)).Select(c => c.DocumentTypeId).ToListAsync(ct).ConfigureAwait(false)
            : await db.Set<DocumentTypeConfTo>().AsNoTracking().Where(c => c.Active && ids.Contains(c.DocumentTypeId) && costCenters.Contains(c.ElementsId)).Select(c => c.DocumentTypeId).ToListAsync(ct).ConfigureAwait(false);
        var typeIds = types.Select(t => t.Id).ToList();
        // A type in several categories (Cash desk and Bank) opens in the first by the categories' order.
        var categories = (await (from l in db.Set<InterDocumentTypeCategory>().AsNoTracking() join c in db.Set<DocumentCategory>().AsNoTracking() on l.CategoryId equals c.Id
                                 where l.Active && typeIds.Contains(l.TypeId) select new { l.TypeId, l.CategoryId, c.SortOrder, c.Code }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.TypeId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).First().CategoryId);
        return [.. types.Where(t => bankSide.Contains(t.Id) && categories.ContainsKey(t.Id)).Select(t => new BankNewDocumentDto(t.Id, categories[t.Id], t.Name.In(language), bankIsFrom))];
    }

    private async Task<BankStatement> StatementAsync(Guid id, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        return await db.Set<BankStatement>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.Active && companies.Contains(s.OwnerId!.Value), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The statement does not exist.");
    }

    private async Task<BankStatementLine> LineAsync(Guid id, CancellationToken ct) =>
        await db.Set<BankStatementLine>().FirstOrDefaultAsync(l => l.Id == id && l.Active, ct).ConfigureAwait(false) ?? throw ValidationException.For("id", "The statement line does not exist.");

    /// <summary>The file's lines; a line that can't be read refuses the whole file, naming its number.</summary>
    public static IReadOnlyList<BankStatementLine> Read(string text, BankStatementFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(format);
        var lines = new List<BankStatementLine>();
        foreach (var (cells, number) in Rows(text, format.Separator).Select((r, i) => (r, i + 1)).Skip(format.HeaderLines))
        {
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string? Cell(int? column) => column is { } c && c >= 1 && c <= cells.Count ? cells[c - 1].Trim() : null;
            if (!DateOnly.TryParseExact(Cell(format.DateColumn), format.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw ValidationException.For("file", $"Line {number}: the date can't be read.");
            }

            var amount = format.AmountColumn is not null ? Number(Cell(format.AmountColumn), format) : Number(Cell(format.MoneyInColumn), format) - (Number(Cell(format.MoneyOutColumn), format) is { } paid ? Math.Abs(paid) : null);
            if (amount is not { } value)
            {
                throw ValidationException.For("file", $"Line {number}: the amount can't be read.");
            }

            if (value != 0)
            {
                lines.Add(new BankStatementLine { LineDate = date, Text = Cell(format.TextColumn), Reference = Cell(format.ReferenceColumn), Amount = value });
            }
        }

        return lines;
    }

    /// <summary>A cell as a number, an empty cell as 0, null when it is not one. Money out may be written negative in its own column.</summary>
    private static decimal? Number(string? cell, BankStatementFormat format)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            return format.AmountColumn is null ? 0 : null;
        }

        var plain = new string([.. cell.Where(c => !char.IsWhiteSpace(c) && c != '\'')]);
        plain = format.DecimalComma ? plain.Replace(".", "", StringComparison.Ordinal).Replace(',', '.') : plain.Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(plain, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>The rows of a separated file: a cell may be quoted, and a quoted cell may hold the separator, quotes doubled, and line ends.</summary>
    private static IEnumerable<IReadOnlyList<string>> Rows(string text, string separator)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    cell.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (string.CompareOrdinal(text, i, separator, 0, separator.Length) == 0)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                i += separator.Length - 1;
            }
            else if (c == '\n')
            {
                cells.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                yield return cells;
                cells = [];
            }
            else
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            yield return cells;
        }
    }
}

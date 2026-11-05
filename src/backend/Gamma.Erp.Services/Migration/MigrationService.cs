using System.Globalization;
using ClosedXML.Excel;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Currencies;
using Gamma.Erp.Services.Depreciation;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>The last file imported into a step.</summary>
public sealed record MigrationRunDto(DateTimeOffset At, string? User, string? FileName, int Added, int Updated);

/// <summary>A step as the screen shows it, in the tree's order: locked while any step above it holds nothing.</summary>
public sealed record MigrationStepDto(Guid Id, string? Code, string? Name, double SortOrder, IReadOnlyList<string> Waits, int Rows, MigrationRunDto? Last, int Level = 0, IReadOnlyList<string>? Needs = null, string? Parent = null);

/// <summary>What is wrong with a cell of the file; row is the sheet's row number.</summary>
public sealed record MigrationErrorDto(int Row, string? Column, string Message);

/// <summary>What a file does: a check saves nothing; a confirmed import saves every row or, with any error, none.</summary>
public sealed record MigrationResultDto(int Rows, int Added, int Updated, bool Saved, IReadOnlyList<MigrationErrorDto> Errors, decimal? OpeningBalance = null);

/// <summary>The account the opening steps balance on, and where it stands: zero once the detail agrees with the chart.</summary>
public sealed record MigrationOpeningDto(string? Account, string? Name, decimal Balance);

/// <summary>The entities the lists read and write, each with its own rules.</summary>
public sealed record MigrationEntities(IReadOnlyDictionary<string, IEntityRows> Rows);

/// <summary>
/// The Migration menu: each step exports what the system holds, gives its empty template, and imports a file. A check and an import are one path: the
/// rows go through the same saves as the screens inside a transaction, and a check rolls it back, so a check finds exactly what an import would.
/// A step waits for the steps it needs (their order is configuration). Whoever has the screen in their menu may use it (Menu access).
/// </summary>
public sealed class MigrationService(ErpDbContext db, RequestContext request, Visibility visibility, MigrationEntities entities, CurrencyService currencies, ProductUnitService units, DocumentService documents, DepreciationService depreciation)
{
    /// <summary>The menu screen that opens this: Menu access on its row decides who uses it.</summary>
    public const string Screen = "migration";

    /// <summary>The kinds of list the engine reads and writes.</summary>
    public static readonly IReadOnlyList<string> Kinds = [ElementsKind, CategoriesKind, PricesKind, RatesKind, NumbersKind, OpeningKind, PostingGroupsKind, DocumentTypesKind, TypesKind, LineKindsKind, OpeningStockKind, OpeningAssetsKind, VatGroupsKind, DocumentVatKind, VatBoxesKind, VatRulesKind];

    /// <summary>The rows one file may hold.</summary>
    public const int MaxRows = 20000;

    /// <summary>The bytes one file may hold.</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    public const string ContentType = Printing.ExcelExport.ContentType;

    private const string ElementsKind = "Elements", CategoriesKind = "Categories", PricesKind = "Prices", RatesKind = "Rates", NumbersKind = "Numbers", OpeningKind = "Opening", PostingGroupsKind = "PostingGroups", DocumentTypesKind = "DocumentTypes", TypesKind = "Types", LineKindsKind = "LineKinds", OpeningStockKind = "OpeningStock", OpeningAssetsKind = "OpeningAssets", VatGroupsKind = "VatGroups", DocumentVatKind = "DocumentVat", VatBoxesKind = "VatBoxes", VatRulesKind = "VatRules";

    private const string DataSheet = "Data", HelpSheet = "Allowed values", Savepoint = "migration_row";

    public async Task<IReadOnlyList<MigrationStepDto>> StepsAsync(string language, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var items = await ItemsAsync(ct).ConfigureAwait(false);
        var tree = MigrationTree.Build(items);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var own = await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            counts[item.Code!] = await ListOf(item, own).CountAsync(ct).ConfigureAwait(false);
        }

        var ids = items.Select(i => i.Id).ToList();
        var runs = await db.Set<MigrationRun>().AsNoTracking().Where(r => ids.Contains(r.MigrationItemId)).OrderByDescending(r => r.CreatedOn).ToListAsync(ct).ConfigureAwait(false);
        var writers = runs.Select(r => r.CreatedBy).OfType<Guid>().Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => writers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        string Named(string code) => items.FirstOrDefault(i => i.Code == code)?.Name?.In(language) ?? code;
        return [.. tree.Select(node =>
        {
            var item = node.Item;
            var run = runs.FirstOrDefault(r => r.MigrationItemId == item.Id);
            var waits = node.Above.Where(n => counts.GetValueOrDefault(n) == 0).Order(StringComparer.Ordinal).Select(Named).ToList();
            return new MigrationStepDto(item.Id, item.Code, item.Name?.In(language), item.SortOrder, waits, counts[item.Code!],
                run is null ? null : new MigrationRunDto(run.CreatedOn, run.CreatedBy is { } by ? users.GetValueOrDefault(by) : null, run.FileName, run.Added, run.Updated), node.Level, [.. node.Needs.Select(Named)], node.Parent);
        })];
    }

    /// <summary>What the system holds, in the template's columns, so a corrected export can be imported back.</summary>
    public async Task<byte[]> ExportAsync(string code, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var list = ListOf(await ItemAsync(code, ct).ConfigureAwait(false), await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false));
        return Book(await list.ColumnsAsync(ct).ConfigureAwait(false), await list.RowsAsync(ct).ConfigureAwait(false));
    }

    public async Task<byte[]> TemplateAsync(string code, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        return Book(await ListOf(await ItemAsync(code, ct).ConfigureAwait(false), await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false)).ColumnsAsync(ct).ConfigureAwait(false), []);
    }

    /// <summary>Where the opening account stands, or nothing when no step balances on one.</summary>
    public async Task<MigrationOpeningDto?> OpeningAsync(string language, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var code = (await ItemsAsync(ct).ConfigureAwait(false)).Select(i => i.OffsetAccount).FirstOrDefault(c => c is not null);
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var account = code is null ? null : await db.Set<Chart>().AsNoTracking().FirstOrDefaultAsync(c => c.Code == code && c.OwnerId != null && nodes.Contains(c.OwnerId.Value), ct).ConfigureAwait(false);
        return account is null ? null : new(account.Code, account.Name.In(language), await OpeningList.OpeningBalanceAsync(db, account).ConfigureAwait(false));
    }

    /// <summary>Checks a file row by row, and with <paramref name="confirm"/> saves it: every row, or none when any is wrong.</summary>
    public async Task<MigrationResultDto> ImportAsync(string code, Stream file, string? fileName, bool confirm, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var item = await ItemAsync(code, ct).ConfigureAwait(false);
        if ((await StepsAsync(TranslatedText.DefaultLanguage, ct).ConfigureAwait(false)).First(s => s.Id == item.Id).Waits is { Count: > 0 } waits)
        {
            throw ValidationException.For("step", $"First fill in: {string.Join(", ", waits)}.");
        }

        var list = ListOf(item, await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false));
        var (rows, sheetErrors) = Read(file, await list.ColumnsAsync(ct).ConfigureAwait(false));
        if (sheetErrors.Count > 0)
        {
            return new(rows.Count, 0, 0, false, sheetErrors);
        }

        await list.PrepareAsync(ct).ConfigureAwait(false);
        var errors = new List<MigrationErrorDto>();
        var (added, updated) = (0, 0);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var (number, row) in rows)
        {
            await transaction.CreateSavepointAsync(Savepoint, ct).ConfigureAwait(false);
            try
            {
                if (await list.SaveAsync(row, ct).ConfigureAwait(false))
                {
                    added++;
                }
                else
                {
                    updated++;
                }
            }
            catch (ValidationException refused)
            {
                errors.AddRange(refused.Errors.Select(e => new MigrationErrorDto(number, Column(e.Key), e.Value)));
                await transaction.RollbackToSavepointAsync(Savepoint, ct).ConfigureAwait(false);
            }
            catch (DbUpdateException refused)
            {
                errors.Add(new(number, null, refused.InnerException?.Message ?? refused.Message));
                await transaction.RollbackToSavepointAsync(Savepoint, ct).ConfigureAwait(false);
            }

            db.ChangeTracker.Clear();
        }

        decimal? opening = null;
        if (errors.Count == 0)
        {
            try
            {
                await list.FinishAsync(ct).ConfigureAwait(false);
                opening = await list.AfterAsync(ct).ConfigureAwait(false);
            }
            catch (ValidationException refused)
            {
                errors.AddRange(refused.Errors.Select(e => new MigrationErrorDto(0, null, e.Value)));
            }
        }

        var save = confirm && errors.Count == 0;
        if (save)
        {
            db.Add(new MigrationRun { MigrationItemId = item.Id, FileName = fileName, Added = added, Updated = updated, DocumentId = list.PostedDocument, TenantId = request.TenantId, OwnerId = item.OwnerId });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
        }

        return new(rows.Count, added, updated, save, errors, opening);
    }

    private async Task AllowedAsync(CancellationToken ct)
    {
        if (!await visibility.OpensScreenAsync(Screen, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: Migration is not in your menu.");
        }
    }

    private async Task<List<MigrationItem>> ItemsAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        return await db.Set<MigrationItem>().AsNoTracking().Where(i => i.Code != null && i.OwnerId != null && nodes.Contains(i.OwnerId.Value)).OrderBy(i => i.SortOrder).ThenBy(i => i.Code)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<MigrationItem> ItemAsync(string code, CancellationToken ct) =>
        (await ItemsAsync(ct).ConfigureAwait(false)).FirstOrDefault(i => i.Code == code) ?? throw ValidationException.For("step", "There is no such step.");

    private MigrationList ListOf(MigrationItem item, string language) => item.Kind switch
    {
        ElementsKind when item.Entity == "products" => new ElementList(Entity(item), db, visibility, language, null, units, entities.Rows["units"]),
        ElementsKind when item.Entity == "charts" => new ElementList(Entity(item), db, visibility, language, null, null, null, AccountRenamedAsync),
        ElementsKind when item.Entity == "cost-centers" => new ElementList(Entity(item), db, visibility, language, () => CompanyNode),
        ElementsKind => new ElementList(Entity(item), db, visibility, language),
        CategoriesKind => new CategoryList(Entity(item), language),
        OpeningKind => new OpeningList(db, request, visibility, documents, currencies, item),
        PostingGroupsKind => new PostingGroupList(db, request, visibility),
        DocumentTypesKind => new DocumentTypeList(Entity(item), db, language),
        TypesKind => new DocumentTypeList(Entity(item), db, language, documents: false),
        LineKindsKind => new LineKindList(db, request, visibility),
        VatGroupsKind => new VatGroupList(db, request, visibility, language),
        DocumentVatKind => new DocumentVatList(db, request, visibility),
        VatBoxesKind => new VatBoxList(db, request, visibility, language),
        VatRulesKind => new VatRuleList(db, request, visibility),
        OpeningStockKind => new OpeningStockList(db, request, visibility, documents, item),
        OpeningAssetsKind => new OpeningAssetList(db, request, visibility, depreciation, new OpeningList(db, request, visibility, documents, currencies, item), item),
        PricesKind => new PriceList(db, request, visibility, currencies),
        RatesKind => new RateList(currencies),
        NumbersKind => new NumberList(db, visibility),
        _ => throw ValidationException.For("kind", $"{ErrorCodes.NotConfiguredError}: the step {item.Code} has the kind {item.Kind}, which can't be read."),
    };

    // Statements find their accounts by the start of the code, so a new code must stay on the same lines; the opening account's code follows it.
    private async Task AccountRenamedAsync(string from, string to, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var lines = (await db.Set<ChartAssetsLiabilities>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).Cast<StatementLine>()
            .Concat(await db.Set<ChartProfitLoss>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).Where(l => l.OwnerId is null || nodes.Contains(l.OwnerId.Value)).ToList();
        foreach (var group in lines.GroupBy(l => l.GetType()))
        {
            var (before, after) = (Line(group, from), Line(group, to));
            if (before != after)
            {
                throw ValidationException.For("New code", $"The new code would move the account from statement line {before ?? "(none)"} to {after ?? "(none)"}: change that line's accounts first.");
            }
        }

        foreach (var item in await db.Set<MigrationItem>().Where(i => i.OffsetAccount == from && i.OwnerId != null && nodes.Contains(i.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false))
        {
            item.OffsetAccount = to;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // The statement lines that hold a code: the longest start that fits, whatever the balance rule (a client in credit moves between lines of one start).
    private static string? Line(IEnumerable<StatementLine> lines, string code)
    {
        var starts = lines.Where(l => !string.IsNullOrWhiteSpace(l.Accounts)).SelectMany(l => l.Accounts!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => (Start: s, l.Code)))
            .Where(s => code.StartsWith(s.Start, StringComparison.Ordinal)).OrderByDescending(s => s.Start.Length).ToList();
        return starts.Count == 0 ? null : string.Join(", ", starts.Where(s => s.Start.Length == starts[0].Start.Length).Select(s => s.Code).Order(StringComparer.Ordinal));
    }

    // A cost centre with no parent sits under the company: a warehouse is never a root.
    private Guid CompanyNode => visibility.CompanyAsync(default).GetAwaiter().GetResult() ?? request.NodeId;

    private IEntityRows Entity(MigrationItem item) => item.Entity is { } name && entities.Rows.TryGetValue(name, out var rows)
        ? rows : throw ValidationException.For("entity", $"{ErrorCodes.NotConfiguredError}: the step {item.Code} names the entity {item.Entity}.");

    private static string? Column(string key) => key.StartsWith("values.", StringComparison.Ordinal) ? null : key is "name" ? "Name" : key is "code" ? "Code" : key is "typeId" ? "Type" : key;

    // The list as a workbook: the data sheet can be filled in and imported; the second says what each column takes. A value goes in as a value, never as a formula.
    private static byte[] Book(IReadOnlyList<MigrationColumn> columns, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        using var book = new XLWorkbook();
        var data = book.Worksheets.Add(DataSheet);
        for (var c = 0; c < columns.Count; c++)
        {
            data.Cell(1, c + 1).Value = columns[c].Header;
            data.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = data.Cell(r + 2, c + 1);
                switch (rows[r].GetValueOrDefault(columns[c].Header))
                {
                    case null:
                        break;
                    case decimal number:
                        cell.Value = number;
                        break;
                    case int number:
                        cell.Value = number;
                        break;
                    case DateOnly day:
                        cell.Value = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        break;
                    case var text:
                        cell.SetValue(text.ToString());
                        break;
                }
            }
        }

        data.SheetView.FreezeRows(1);
        data.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
        var help = book.Worksheets.Add(HelpSheet);
        (help.Cell(1, 1).Value, help.Cell(1, 2).Value, help.Cell(1, 3).Value) = ("Column", "Required", "Takes");
        help.Row(1).Style.Font.Bold = true;
        for (var c = 0; c < columns.Count; c++)
        {
            help.Cell(c + 2, 1).Value = columns[c].Header;
            help.Cell(c + 2, 2).Value = columns[c].Required ? "Yes" : "No";
            help.Cell(c + 2, 3).Value = columns[c].Help;
        }

        help.Column(1).Width = 28;
        help.Column(3).Width = 100;
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static (List<(int Number, Dictionary<string, string> Cells)> Rows, List<MigrationErrorDto> Errors) Read(Stream file, IReadOnlyList<MigrationColumn> columns)
    {
        var rows = new List<(int, Dictionary<string, string>)>();
        var errors = new List<MigrationErrorDto>();
        XLWorkbook book;
        try
        {
            book = new XLWorkbook(file);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or FormatException or IOException or InvalidOperationException)
        {
            errors.Add(new(0, null, "The file is not an Excel workbook (.xlsx)."));
            return (rows, errors);
        }

        using (book)
        {
            var sheet = book.Worksheets.FirstOrDefault(s => s.Name == DataSheet) ?? book.Worksheet(1);
            var used = sheet.RangeUsed();
            if (used is null)
            {
                errors.Add(new(0, null, "The sheet is empty."));
                return (rows, errors);
            }

            var headings = new Dictionary<int, string>();
            for (var c = 1; c <= used.LastColumn().ColumnNumber(); c++)
            {
                var heading = sheet.Cell(1, c).GetString().Trim();
                if (heading.Length == 0)
                {
                    continue;
                }

                var known = columns.FirstOrDefault(k => string.Equals(k.Header, heading, StringComparison.OrdinalIgnoreCase));
                if (known is null)
                {
                    errors.Add(new(1, heading, "This column is not in the template."));
                }
                else if (headings.ContainsValue(known.Header))
                {
                    errors.Add(new(1, heading, "This column appears twice."));
                }
                else
                {
                    headings[c] = known.Header;
                }
            }

            errors.AddRange(columns.Where(c => c.Required && !headings.ContainsValue(c.Header)).Select(c => new MigrationErrorDto(1, c.Header, "This required column is missing.")));
            var last = used.LastRow().RowNumber();
            if (last - 1 > MaxRows)
            {
                errors.Add(new(0, null, $"A file holds at most {MaxRows} rows."));
            }

            if (errors.Count > 0)
            {
                return (rows, errors);
            }

            for (var r = 2; r <= last; r++)
            {
                var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (c, heading) in headings)
                {
                    var cell = sheet.Cell(r, c);
                    if (cell.HasFormula)
                    {
                        errors.Add(new(r, heading, "A formula is not read: paste the value."));
                        continue;
                    }

                    cells[heading] = Text(cell.Value);
                }

                if (cells.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                {
                    rows.Add((r, cells));
                }
            }

            return (rows, errors);
        }
    }

    private static string Text(XLCellValue value) => value.Type switch
    {
        XLDataType.Number => value.GetNumber().ToString(CultureInfo.InvariantCulture),
        XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        XLDataType.Boolean => value.GetBoolean() ? "Yes" : "No",
        XLDataType.Text => value.GetText(),
        _ => string.Empty,
    };
}

using Gamma.Erp.Models.Depreciation;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Depreciation;

/// <summary>
/// An asset to register in a book. Months and percentages left
/// out are the ones the book's depreciation scenario for the asset proposes (its category's: the country's rates).
/// </summary>
public sealed record AssetRegistration(Guid ProductId, Guid UnitId, Guid BookId, decimal InitialValue, DateOnly StartDate, int? Months = null,
    Guid? DocumentId = null, Guid? MethodId = null, decimal? LinearPercentage = null, decimal? DecliningPercentage = null, Guid? DefaultCostCenterId = null);

/// <summary>A depreciation book (B11): its name, method, chart and whether its journals post.</summary>
public sealed record DepreciationBookDto(Guid Id, string Name, Guid? MethodId, string? Method, Guid? ChartCategoryId, bool Posts);

/// <summary>One asset value's depreciation for a run.</summary>
public sealed record DepreciationLine(Guid AssetValueId, Guid ProductId, string? ProductName, Guid? CostCenterId, DateOnly From, DateOnly To, int Days,
    decimal Amount, decimal BookValueBefore, decimal BookValueAfter);

/// <summary>An asset value in a book: what it was worth, what was depreciated, its book value now.</summary>
public sealed record AssetRowDto(Guid AssetValueId, Guid ProductId, string? Code, string? Product, DateOnly? StartDate, DateOnly? EndDate, string? Method, decimal InitialValue,
    decimal Depreciated, decimal BookValue, DateOnly? LastRun, bool Disposed, bool Sold = false);

/// <summary>One period of an asset's depreciation.</summary>
public sealed record AssetRunRowDto(DateOnly? From, DateOnly? To, Guid? ProductId, string? Product, decimal Amount, decimal BookValue, Guid? DocumentId, string? Document, bool Posted);

/// <summary>A run of a book: its date, assets, amount and journal; the latest is the one that can be undone.</summary>
public sealed record DepreciationRunDto(DateOnly Date, int Assets, decimal Amount, Guid? DocumentId, string? Document, bool Posted, bool Latest);

/// <summary>
/// Depreciation (registering, calculating, the journal), every rule from configuration:
/// <list type="bullet">
/// <item>An asset is a product (product × unit) with a value in a book (tbl_asset_value; its end date, percentages and disposal in
/// tbl_asset_value_unique). A book (tbl_depreciation_book) has its method and says whether it posts.</item>
/// <item>A run's amount per asset value is the method's formula (tbl_depreciation_method.formula) with VF the initial value, DT the asset's
/// total days, DK the days of this run's period (since its last depreciation, or its start), YK the full years since its start, PD / PL the
/// declining / linear percentage as a fraction. The book value never goes below zero.</item>
/// <item>The journal: a document of the given type with, per asset, the lines of the book's depreciation scenario that fits it best (its
/// scope: the product's table, category, type, the product itself), each the run's amount on its account and side; posted when the book
/// posts.</item>
/// </list>
/// </summary>
public sealed class DepreciationService(ErpDbContext db, RequestContext request, DocumentService documents)
{
    /// <summary>The depreciation books the request's company uses (B11, C3 phase 7): its own and shared ones, with their method's name.</summary>
    public async Task<IReadOnlyList<DepreciationBookDto>> BooksAsync(string language, CancellationToken ct)
    {
        var nodes = await new Visibility(db, request).ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var books = await db.Set<DepreciationBook>().AsNoTracking().Where(b => b.OwnerId == null || nodes.Contains(b.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false);
        var methodIds = books.Where(b => b.MethodId != null).Select(b => b.MethodId!.Value).ToList();
        var methods = await db.Set<DepreciationMethod>().AsNoTracking().Where(m => methodIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        return [.. books.Select(b => new DepreciationBookDto(b.Id, b.Name?.In(language) ?? "", b.MethodId,
            b.MethodId is { } m && methods.TryGetValue(m, out var method) ? method.Name?.In(language) : null, b.ChartCategoryId, b.Posts)).OrderBy(b => b.Name)];
    }

    public async Task<IReadOnlyList<Guid>> RegisterAsync(AssetRegistration asset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(asset);
        // Only a product of a fixed-asset type is registered (each product type says whether it is one).
        if (!await (from p in db.Set<Product>().AsNoTracking() join t in db.Set<ProductType>().AsNoTracking() on p.TypeId equals t.Id
                    where p.Id == asset.ProductId && t.FixedAsset select p.Id).AnyAsync(ct).ConfigureAwait(false))
        {
            throw ValidationException.For("productId", "The product's type is not a fixed asset type.");
        }

        if (asset.Months is null || asset.LinearPercentage is null || asset.DecliningPercentage is null)
        {
            var proposed = await BestScenarioAsync(asset.BookId, asset.ProductId, ct).ConfigureAwait(false);
            asset = asset with
            {
                Months = asset.Months ?? proposed?.Months, LinearPercentage = asset.LinearPercentage ?? proposed?.LinearPercentage,
                DecliningPercentage = asset.DecliningPercentage ?? proposed?.DecliningPercentage,
            };
        }

        if (asset.InitialValue <= 0 || asset.Months is not > 0)
        {
            throw ValidationException.For("initialValue", "An asset needs a value and a number of months to depreciate over (none is proposed for its category: land is not depreciated).");
        }

        var months = asset.Months.Value;

        var book = await db.Set<DepreciationBook>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == asset.BookId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("bookId", "The book does not exist.");
        var specific = await db.Set<InterProductSpecificUnit>().FirstOrDefaultAsync(s => s.ProductId == asset.ProductId && s.UnitId == asset.UnitId, ct).ConfigureAwait(false);
        if (specific is null)
        {
            specific = new InterProductSpecificUnit { ProductId = asset.ProductId, UnitId = asset.UnitId, OwnerId = request.NodeId };
            db.Add(specific);
        }

        var company = await new Visibility(db, request).CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("ownerId", "Assets belong to a company.");
        var value = new AssetValue
        {
            CompanyId = company, ProductSpecificUnitId = specific.Id, DocumentId = asset.DocumentId, BookId = book.Id, MethodId = asset.MethodId ?? book.MethodId,
            StartDate = asset.StartDate, Months = months, Years = months / 12m, InitialValue = asset.InitialValue, DefaultCostCenterId = asset.DefaultCostCenterId, OwnerId = request.NodeId,
        };
        db.Add(value);
        var unique = await db.Set<AssetValueUnique>().FirstOrDefaultAsync(u => u.ProductSpecificUnitId == specific.Id && u.BookId == book.Id, ct).ConfigureAwait(false);
        if (unique is null)
        {
            db.Add(new AssetValueUnique
            {
                CompanyId = company, ProductSpecificUnitId = specific.Id, BookId = book.Id, MethodId = value.MethodId, EndDate = asset.StartDate.AddMonths(months),
                LinearPercentage = asset.LinearPercentage ?? 0, DecliningPercentage = asset.DecliningPercentage ?? 0, OwnerId = request.NodeId,
            });
        }
        else
        {
            // A further value of the same asset: its end date runs to the later of the two, its percentages are the newest registration's.
            var end = asset.StartDate.AddMonths(months);
            unique.EndDate = unique.EndDate is { } before && before > end ? before : end;
            (unique.LinearPercentage, unique.DecliningPercentage) = (asset.LinearPercentage ?? 0, asset.DecliningPercentage ?? 0);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return [value.Id];
    }

    /// <summary>The run a book would have on a date: per asset value not disposed, its period's amount by the formula.</summary>
    public async Task<IReadOnlyList<DepreciationLine>> CalculateAsync(Guid bookId, DateOnly date, CancellationToken ct)
    {
        var book = await db.Set<DepreciationBook>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("bookId", "The book does not exist.");
        var methods = await db.Set<DepreciationMethod>().AsNoTracking().ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        var values = await db.Set<AssetValue>().AsNoTracking().Where(v => v.BookId == bookId && v.SoldOn == null).ToListAsync(ct).ConfigureAwait(false);
        var specificIds = values.Select(v => v.ProductSpecificUnitId).ToList();
        var uniques = (await db.Set<AssetValueUnique>().AsNoTracking().Where(u => u.BookId == bookId && specificIds.Contains(u.ProductSpecificUnitId)).ToListAsync(ct)
            .ConfigureAwait(false)).ToDictionary(u => u.ProductSpecificUnitId!.Value);
        var specifics = await db.Set<InterProductSpecificUnit>().AsNoTracking().Where(s => specificIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        var productIds = specifics.Values.Select(s => s.ProductId).ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var valueIds = values.Select(v => (Guid?)v.Id).ToList();
        var last = (await db.Set<AssetDepreciation>().AsNoTracking().Where(d => valueIds.Contains(d.AssetValueId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(d => d.AssetValueId!.Value).ToDictionary(g => g.Key, g => g.MaxBy(d => d.DepreciationDate)!);
        var holders = (await db.Set<InterCostCenterProductStock>().AsNoTracking().Where(s => specificIds.Contains(s.ProductSpecificUnitId) && s.Quantity > 0)
            .ToListAsync(ct).ConfigureAwait(false)).GroupBy(s => s.ProductSpecificUnitId!.Value).ToDictionary(g => g.Key, g => g.First().CostCenterId);

        var lines = new List<DepreciationLine>();
        foreach (var value in values)
        {
            var specific = value.ProductSpecificUnitId!.Value;
            var unique = uniques.GetValueOrDefault(specific);
            if (unique?.Disposed == true || value.StartDate is not { } start || value.InitialValue is not { } initial)
            {
                continue;
            }

            var end = unique?.EndDate ?? start.AddMonths((int)(value.Months ?? 0));
            var previous = last.GetValueOrDefault(value.Id);
            var from = previous?.DepreciationDate ?? start;
            var to = date < end ? date : end;
            var days = to.DayNumber - from.DayNumber;
            if (days <= 0)
            {
                continue;
            }

            var method = methods.GetValueOrDefault(value.MethodId ?? book.MethodId ?? Guid.Empty)
                ?? throw ValidationException.For("methodId", $"{ErrorCodes.NotConfiguredError}: the asset value has no depreciation method.");
            var years = (from.Year - start.Year) - (from.DayOfYear < start.DayOfYear ? 1 : 0);
            var amount = Formula.Evaluate(method.Formula ?? string.Empty, new Dictionary<string, decimal>
            {
                ["VF"] = initial, ["DT"] = end.DayNumber - start.DayNumber, ["DK"] = days, ["YK"] = Math.Max(years, 0),
                ["PD"] = (unique?.DecliningPercentage ?? 0) / 100, ["PL"] = (unique?.LinearPercentage ?? 0) / 100,
            });
            var before = previous?.BookValue ?? initial;
            amount = Math.Round(Math.Clamp(amount, 0, before), 2);
            var product = specifics[specific].ProductId!.Value;
            lines.Add(new DepreciationLine(value.Id, product, products.GetValueOrDefault(product)?.Name.In("en"), value.DefaultCostCenterId ?? holders.GetValueOrDefault(specific), from, to, days,
                amount, before, before - amount));
        }

        return lines;
    }

    /// <summary>Saves a book's run on a date: one row per asset value, not yet in a journal.</summary>
    public async Task<IReadOnlyList<DepreciationLine>> RunAsync(Guid bookId, DateOnly date, CancellationToken ct)
    {
        await ClosedPeriodAsync(date, ct).ConfigureAwait(false);
        var lines = await CalculateAsync(bookId, date, ct).ConfigureAwait(false);
        var company = await new Visibility(db, request).CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("ownerId", "Depreciation runs in a company.");
        foreach (var line in lines.Where(l => l.Amount > 0))
        {
            db.Add(new AssetDepreciation
            {
                CompanyId = company, AssetValueId = line.AssetValueId, CostCenterId = line.CostCenterId, DepreciationStartDate = line.From, DepreciationDate = line.To,
                DepreciationValue = line.Amount, BookValue = line.BookValueAfter, PostedValue = line.Amount, OwnerId = request.NodeId,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return lines;
    }

    /// <summary>
    /// The journal of a book's run on a date: a document of the given type with, per asset, the lines of the book's
    /// depreciation scenario that fits it best, each the run's amount on its account and side (on the VAT group marked for depreciation);
    /// posted when the book posts. The run's rows are linked to it.
    /// </summary>
    public async Task<DocumentDto> JournalAsync(Guid bookId, DateOnly date, Guid documentTypeId, Guid categoryId, CancellationToken ct)
    {
        await ClosedPeriodAsync(date, ct).ConfigureAwait(false);
        var book = await db.Set<DepreciationBook>().AsNoTracking().FirstAsync(b => b.Id == bookId, ct).ConfigureAwait(false);
        var bookValues = db.Set<AssetValue>().Where(v => v.BookId == bookId).Select(v => (Guid?)v.Id);
        var rows = await db.Set<AssetDepreciation>().Where(d => d.DepreciationDate == date && d.DocumentId == null && bookValues.Contains(d.AssetValueId))
            .ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            throw ValidationException.For("date", "The book has no depreciation on this date that is not in a journal yet.");
        }

        var chartTable = db.Model.FindEntityType(typeof(Models.Charts.Chart))!.GetTableName();
        var gl = await (from l in db.Set<InterDocumentTypeTransactionType>() join t in db.Set<TransactionType>() on l.TransactionTypeId equals t.Id
                        join c in db.Set<TransactionTypeConf>() on (Guid?)t.Id equals c.TransactionTypeId
                        join e in db.Set<Elements>() on c.ElementsId equals e.Id
                        where l.DocumentTypeId == documentTypeId && e.Table == chartTable select t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (gl == Guid.Empty)
        {
            throw ValidationException.For("documentTypeId", $"{ErrorCodes.NotConfiguredError}: the journal's document type has no transaction type on accounts.");
        }

        var owner = await new Visibility(db, request).CompanyAsync(ct).ConfigureAwait(false);
        var vat = await db.Set<VatGroup>().AsNoTracking().Where(v => v.Depreciation && v.Active && v.OwnerId == owner).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var lines = new List<TransactionDto>();
        foreach (var row in rows)
        {
            var value = await db.Set<AssetValue>().AsNoTracking().FirstAsync(v => v.Id == row.AssetValueId, ct).ConfigureAwait(false);
            var specific = await db.Set<InterProductSpecificUnit>().AsNoTracking().FirstAsync(s => s.Id == value.ProductSpecificUnitId, ct).ConfigureAwait(false);
            var conf = await ScenarioLinesAsync(bookId, specific.ProductId!.Value, ct).ConfigureAwait(false);
            var chartIds = conf.Select(l => l.ChartId).ToList();
            var chartTypes = await db.Set<Models.Charts.Chart>().AsNoTracking().Where(c => chartIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.TypeId, ct).ConfigureAwait(false);
            foreach (var line in conf)
            {
                var amount = row.PostedValue ?? 0;
                lines.Add(new TransactionDto
                {
                    TypeId = gl, ElementId = line.ChartId, ElementTypeId = chartTypes.GetValueOrDefault(line.ChartId!.Value), ElementName = line.ChartName, Price = amount, Quantity = 1, VatGroupId = vat, FromId = row.CostCenterId,
                    Debit = line.Side ? 0 : amount, Credit = line.Side ? amount : 0,
                });
            }
        }

        // A journal type under another (a depreciation journal under its sheet) is a document under a sheet of the parent type.
        var parentType = await db.Set<DocumentType>().AsNoTracking().Where(t => t.Id == documentTypeId).Select(t => t.IdSup).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        // All or nothing: a posting refused leaves no draft journal behind.
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        DocumentDto? sheet = null;
        if (parentType is { } sheetType)
        {
            sheet = await documents.SaveAsync(new DocumentDto
            {
                TypeId = sheetType, CategoryIds = [categoryId], DocumentDate = date, PostingDate = date, Name = new() { ["en"] = $"Depreciation sheet {date:yyyy-MM-dd}" },
            }, ct).ConfigureAwait(false);
        }

        var journal = await documents.SaveAsync(new DocumentDto
        {
            TypeId = documentTypeId, IdSup = sheet?.Id, CategoryIds = [categoryId], DocumentDate = date, PostingDate = date, FromId = request.NodeId, ToId = request.NodeId,
            Name = new() { ["en"] = $"Depreciation {date:yyyy-MM-dd}" }, Transactions = lines,
        }, ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            row.DocumentId = journal.Id;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (book.Posts)
        {
            journal = await documents.PostAsync(journal.Id!.Value, ct).ConfigureAwait(false);
            if (sheet is not null)
            {
                await documents.PostAsync(sheet.Id!.Value, ct).ConfigureAwait(false);
            }

            foreach (var row in rows)
            {
                row.Posted = true;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return journal;
    }

    /// <summary>The assets of a book, with what was depreciated so far and their book value.</summary>
    public async Task<IReadOnlyList<AssetRowDto>> AssetsAsync(Guid bookId, string language, CancellationToken ct)
    {
        var book = await db.Set<DepreciationBook>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("bookId", "The book does not exist.");
        var values = await db.Set<AssetValue>().AsNoTracking().Where(v => v.BookId == bookId).OrderBy(v => v.StartDate).ToListAsync(ct).ConfigureAwait(false);
        var specificIds = values.Select(v => v.ProductSpecificUnitId).ToList();
        var uniques = (await db.Set<AssetValueUnique>().AsNoTracking().Where(u => u.BookId == bookId && specificIds.Contains(u.ProductSpecificUnitId)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(u => u.ProductSpecificUnitId!.Value);
        var specifics = await db.Set<InterProductSpecificUnit>().AsNoTracking().Where(s => specificIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        var productIds = specifics.Values.Select(s => s.ProductId).ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var methods = await db.Set<DepreciationMethod>().AsNoTracking().ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        var valueIds = values.Select(v => (Guid?)v.Id).ToList();
        var runs = (await db.Set<AssetDepreciation>().AsNoTracking().Where(d => valueIds.Contains(d.AssetValueId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(d => d.AssetValueId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        return [.. values.Select(v =>
        {
            var specific = v.ProductSpecificUnitId!.Value;
            var unique = uniques.GetValueOrDefault(specific);
            var product = specifics[specific].ProductId!.Value;
            var done = runs.GetValueOrDefault(v.Id, []);
            var depreciated = done.Sum(d => d.DepreciationValue ?? 0);
            var (end, name) = (unique?.EndDate ?? v.StartDate?.AddMonths((int)(v.Months ?? 0)), products.GetValueOrDefault(product));
            return new AssetRowDto(v.Id, product, name?.Code, name?.Name.In(language), v.StartDate, end, methods.GetValueOrDefault(v.MethodId ?? book.MethodId ?? Guid.Empty)?.Name?.In(language),
                v.InitialValue ?? 0, depreciated, (v.InitialValue ?? 0) - depreciated, done.Count == 0 ? null : done.Max(d => d.DepreciationDate), unique?.Disposed == true || v.SoldOn is not null, v.SoldOn is not null);
        })];
    }

    /// <summary>An asset's depreciation, oldest first.</summary>
    public async Task<IReadOnlyList<AssetRunRowDto>> AssetHistoryAsync(Guid assetValueId, CancellationToken ct) =>
        await RunRowsAsync(db.Set<AssetDepreciation>().AsNoTracking().Where(d => d.AssetValueId == assetValueId), "en", ct).ConfigureAwait(false);

    /// <summary>The runs of a book, the latest first.</summary>
    public async Task<IReadOnlyList<DepreciationRunDto>> RunsAsync(Guid bookId, CancellationToken ct)
    {
        var bookValues = db.Set<AssetValue>().Where(v => v.BookId == bookId).Select(v => (Guid?)v.Id);
        var rows = await db.Set<AssetDepreciation>().AsNoTracking().Where(d => bookValues.Contains(d.AssetValueId) && d.DepreciationDate != null).ToListAsync(ct).ConfigureAwait(false);
        var documents = rows.Select(r => r.DocumentId).OfType<Guid>().Distinct().ToList();
        var codes = await db.Set<Document>().AsNoTracking().Where(d => documents.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct).ConfigureAwait(false);
        var latest = rows.Count == 0 ? default : rows.Max(r => r.DepreciationDate!.Value);
        return [.. rows.GroupBy(r => (Date: r.DepreciationDate!.Value, r.DocumentId)).OrderByDescending(g => g.Key.Date).Select(g => new DepreciationRunDto(g.Key.Date, g.Count(),
            g.Sum(r => r.DepreciationValue ?? 0), g.Key.DocumentId, g.Key.DocumentId is { } id ? codes.GetValueOrDefault(id) : null, g.All(r => r.Posted), g.Key.Date == latest))];
    }

    /// <summary>The assets of a book's run on a date.</summary>
    public async Task<IReadOnlyList<AssetRunRowDto>> RunLinesAsync(Guid bookId, DateOnly date, string language, CancellationToken ct)
    {
        var bookValues = db.Set<AssetValue>().Where(v => v.BookId == bookId).Select(v => (Guid?)v.Id);
        return await RunRowsAsync(db.Set<AssetDepreciation>().AsNoTracking().Where(d => bookValues.Contains(d.AssetValueId) && d.DepreciationDate == date), language, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Undoes the latest run of a book (the founder, 2026-11-01): without a journal the run goes; with a posted journal the journal is reversed the usual
    /// way, which cancels the run; with an unposted one the journal goes with it. The assets are back at their book value before the run.
    /// </summary>
    public async Task UndoRunAsync(Guid bookId, DateOnly date, CancellationToken ct)
    {
        var bookValues = db.Set<AssetValue>().Where(v => v.BookId == bookId).Select(v => (Guid?)v.Id);
        var rows = await db.Set<AssetDepreciation>().Where(d => bookValues.Contains(d.AssetValueId)).ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count == 0 || rows.Max(r => r.DepreciationDate) != date)
        {
            throw ValidationException.For("date", "Only the latest run of a book can be undone.");
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var run = rows.Where(r => r.DepreciationDate == date).ToList();
        var journals = run.Select(r => r.DocumentId).OfType<Guid>().Distinct().ToList();
        foreach (var journal in await db.Set<Document>().AsNoTracking().Where(d => journals.Contains(d.Id)).ToListAsync(ct).ConfigureAwait(false))
        {
            var top = journal.IdSup ?? journal.Id;
            if (journal.Posted)
            {
                await documents.ReverseAsync(top, null, date, ct).ConfigureAwait(false);
            }
            else
            {
                await documents.DeleteAsync(top, ct).ConfigureAwait(false);
            }
        }

        run.ForEach(r => r.Active = false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<List<AssetRunRowDto>> RunRowsAsync(IQueryable<AssetDepreciation> query, string language, CancellationToken ct)
    {
        var rows = await query.OrderBy(d => d.DepreciationDate).ToListAsync(ct).ConfigureAwait(false);
        var valueIds = rows.Select(r => r.AssetValueId).ToList();
        var specifics = await (from v in db.Set<AssetValue>().AsNoTracking() join s in db.Set<InterProductSpecificUnit>().AsNoTracking() on v.ProductSpecificUnitId equals s.Id
                               where valueIds.Contains(v.Id) select new { v.Id, s.ProductId }).ToDictionaryAsync(x => x.Id, x => x.ProductId, ct).ConfigureAwait(false);
        var productIds = specifics.Values.ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct).ConfigureAwait(false);
        var documentIds = rows.Select(r => r.DocumentId).OfType<Guid>().Distinct().ToList();
        var codes = await db.Set<Document>().AsNoTracking().Where(d => documentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct).ConfigureAwait(false);
        return [.. rows.Select(r =>
        {
            var product = r.AssetValueId is { } value ? specifics.GetValueOrDefault(value) : null;
            return new AssetRunRowDto(r.DepreciationStartDate, r.DepreciationDate, product, product is { } id ? products.GetValueOrDefault(id)?.In(language) : null, r.DepreciationValue ?? 0,
                r.BookValue ?? 0, r.DocumentId, r.DocumentId is { } document ? codes.GetValueOrDefault(document) : null, r.Posted);
        })];
    }

    /// <summary>Marks an asset disposed in its book, or back in use, by hand: a disposed asset is no longer depreciated. A sold asset is closed by its sale instead.</summary>
    public async Task SetDisposedAsync(Guid assetValueId, bool disposed, CancellationToken ct)
    {
        var company = await new Visibility(db, request).CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("ownerId", "Assets belong to a company.");
        var value = await db.Set<AssetValue>().AsNoTracking().FirstOrDefaultAsync(v => v.Id == assetValueId && v.CompanyId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The asset does not exist.");
        if (value.SoldOn is not null)
        {
            throw ValidationException.For("id", "This asset is sold: its sale closed it.");
        }

        var unique = await db.Set<AssetValueUnique>().FirstOrDefaultAsync(u => u.ProductSpecificUnitId == value.ProductSpecificUnitId && u.BookId == value.BookId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The asset has no book settings to mark.");
        unique.Disposed = disposed;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>An asset's remaining value in a book: its values minus their depreciation.</summary>
    public async Task<decimal> RemainingValueAsync(Guid productId, Guid unitId, Guid bookId, CancellationToken ct)
    {
        var specific = db.Set<InterProductSpecificUnit>().Where(s => s.ProductId == productId && s.UnitId == unitId).Select(s => (Guid?)s.Id);
        var values = await db.Set<AssetValue>().AsNoTracking().Where(v => v.BookId == bookId && v.SoldOn == null && specific.Contains(v.ProductSpecificUnitId)).ToListAsync(ct).ConfigureAwait(false);
        var ids = values.Select(v => (Guid?)v.Id).ToList();
        var depreciated = await db.Set<AssetDepreciation>().AsNoTracking().Where(d => ids.Contains(d.AssetValueId)).SumAsync(d => d.DepreciationValue ?? 0, ct).ConfigureAwait(false);
        return values.Sum(v => v.InitialValue ?? 0) - depreciated;
    }

    /// <summary>
    /// The lines of the book's depreciation scenario that fits a product best:
    /// its scope's table is the product's, and its category, type and record, when set, are the product's.
    /// </summary>
    private async Task<List<DepreciationScenarioConf>> ScenarioLinesAsync(Guid bookId, Guid productId, CancellationToken ct)
    {
        var best = await BestScenarioAsync(bookId, productId, ct).ConfigureAwait(false) ?? throw ValidationException.For("scenario",
            $"{ErrorCodes.NoScenarioError}: no depreciation scenario of the book fits {(await db.Set<Product>().AsNoTracking().FirstAsync(p => p.Id == productId, ct).ConfigureAwait(false)).Name.In("en")}.");
        return await db.Set<DepreciationScenarioConf>().AsNoTracking().Where(l => l.DepreciationScenarioId == best.Id && l.ChartId != null).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A run or its journal on a day of a closed period is refused, as posting is.</summary>
    private async Task ClosedPeriodAsync(DateOnly date, CancellationToken ct)
    {
        var company = await new Visibility(db, request).CompanyAsync(ct).ConfigureAwait(false);
        var until = await db.Set<PostingLock>().AsNoTracking().Where(l => l.OwnerId == company && l.Active).Select(l => (DateOnly?)l.LockedUntil).MaxAsync(ct).ConfigureAwait(false);
        if (until is { } closed && date <= closed)
        {
            throw ValidationException.For("date", $"{ErrorCodes.PeriodClosedError}: {date:yyyy-MM-dd} is in a closed period (up to {closed:yyyy-MM-dd}).");
        }
    }

    /// <summary>The products of the fixed-asset types, for the asset screens.</summary>
    public async Task<IReadOnlyList<OptionDto>> AssetProductsAsync(string language, CancellationToken ct) =>
        [.. (await (from p in db.Set<Product>().AsNoTracking() join t in db.Set<ProductType>().AsNoTracking() on p.TypeId equals t.Id
                    where p.Active && t.FixedAsset select p).ToListAsync(ct).ConfigureAwait(false))
            .Select(p => new OptionDto(p.Id, p.Code, p.Name.In(language) ?? p.Code ?? "")).OrderBy(p => p.Name)];

    /// <summary>The book's depreciation scenario that fits the product best (the most specific scope), or null.</summary>
    private async Task<DepreciationScenario?> BestScenarioAsync(Guid bookId, Guid productId, CancellationToken ct)
    {
        var product = await db.Set<Product>().AsNoTracking().FirstAsync(p => p.Id == productId, ct).ConfigureAwait(false);
        // The product's categories and those above them, each with how far up it is (its own: 0): a scope on its own category (Buildings)
        // fits better than one on the category above (Assets).
        var linked = await db.Set<InterProductCategory>().AsNoTracking().Where(l => l.ElementId == productId).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var tree = await db.Set<ProductCategory>().AsNoTracking().Where(c => c.Active).ToDictionaryAsync(c => c.Id, c => c.IdSup, ct).ConfigureAwait(false);
        var distance = new Dictionary<Guid, int>();
        foreach (var category in linked)
        {
            for (var (at, up) = ((Guid?)category, 0); at is { } c && up < 64; (at, up) = (tree.GetValueOrDefault(c), up + 1))
            {
                distance[c] = Math.Min(distance.GetValueOrDefault(c, int.MaxValue), up);
            }
        }

        var table = db.Model.FindEntityType(typeof(Product))!.GetTableName();
        var candidates = await (from s in db.Set<DepreciationScenario>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on s.ElementsId equals e.Id
                                where s.DepreciationBookId == bookId && s.Active && e.Table == table select new { Scenario = s, Scope = e }).ToListAsync(ct).ConfigureAwait(false);
        return candidates
            .Where(c => (c.Scope.CategoryId is not { } k || distance.ContainsKey(k)) && (c.Scope.TypeId is null || c.Scope.TypeId == product.TypeId)
                && (c.Scope.RecordId is null || c.Scope.RecordId == productId))
            .OrderByDescending(c => c.Scope.RecordId is not null).ThenByDescending(c => c.Scope.TypeId is not null).ThenByDescending(c => c.Scope.CategoryId is not null)
            .ThenBy(c => c.Scope.CategoryId is { } k ? distance[k] : 0)
            .FirstOrDefault()?.Scenario;
    }
}

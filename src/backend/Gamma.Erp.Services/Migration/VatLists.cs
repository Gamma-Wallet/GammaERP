using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>What the VAT lists look rows up by: a group by its code or, as the packs give none, its English name; a document type by code and the type above it.</summary>
internal static class VatLookups
{
    public static string? NameOf(VatGroup group) => group.Code ?? group.Name?.In(TranslatedText.DefaultLanguage);

    public static async Task<List<VatGroup>> GroupsAsync(ErpDbContext db, Visibility visibility, CancellationToken ct, bool tracked = false)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var rows = db.Set<VatGroup>().Where(v => v.OwnerId != null && nodes.Contains(v.OwnerId.Value));
        return await (tracked ? rows : rows.AsNoTracking()).OrderByDescending(v => v.Rate).ToListAsync(ct).ConfigureAwait(false);
    }

    public static async Task<List<DocumentType>> DocumentTypesAsync(ErpDbContext db, Visibility visibility, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        return await db.Set<DocumentType>().AsNoTracking().Where(t => t.OwnerId != null && nodes.Contains(t.OwnerId.Value)).OrderBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false);
    }

    public static DocumentType? Find(IReadOnlyList<DocumentType> types, string code, string? parent)
    {
        var same = types.Where(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)).ToList();
        return same.Count == 1 ? same[0] : same.FirstOrDefault(t => parent is null ? t.IdSup is null
            : types.FirstOrDefault(c => c.Id == t.IdSup) is { } up && string.Equals(up.Code, parent, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The VAT groups: a group is matched by its code, or by its English name when it has none.</summary>
public sealed class VatGroupList(ErpDbContext db, RequestContext request, Visibility visibility, string language) : MigrationList
{
    private const string Code = nameof(Code), Name = nameof(Name), Rate = nameof(Rate), Depreciation = nameof(Depreciation), Yes = nameof(Yes), No = nameof(No);

    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public override async Task<int> CountAsync(CancellationToken ct) => (await VatLookups.GroupsAsync(db, visibility, ct).ConfigureAwait(false)).Count;

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        var columns = new List<MigrationColumn> { new(Code, false, "The group's code, if it has one."), new(Name, true, "The name in English: rows with no code are matched by it."), new(Rate, true, "The rate in percent."), new(Depreciation, false, $"{Yes} or {No}: the group of an asset's depreciation.") };
        if (LocalHeader(language) is { } local)
        {
            columns.Insert(2, new(local, true, $"The name in the company's language ({language})."));
        }

        return Task.FromResult<IReadOnlyList<MigrationColumn>>(columns);
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct) =>
        [.. (await VatLookups.GroupsAsync(db, visibility, ct).ConfigureAwait(false)).Select(g =>
        {
            var row = new Dictionary<string, object?> { [Code] = g.Code, [Name] = g.Name?.In(TranslatedText.DefaultLanguage), [Rate] = g.Rate, [Depreciation] = g.Depreciation ? Yes : No };
            if (LocalHeader(language) is { } local)
            {
                row[local] = g.Name?.Values.GetValueOrDefault(language);
            }

            return row;
        })];

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var key = Cell(row, Code) ?? Needed(row, Name);
        if (!_seen.Add(key))
        {
            throw ValidationException.For(Name, "appears twice in the file.");
        }

        var groups = await VatLookups.GroupsAsync(db, visibility, ct, tracked: true).ConfigureAwait(false);
        var stored = groups.FirstOrDefault(g => string.Equals(VatLookups.NameOf(g), key, StringComparison.OrdinalIgnoreCase));
        var rate = Number(row, Rate);
        if (rate is < 0 or > 100)
        {
            throw ValidationException.For(Rate, "is a percentage, from 0 to 100.");
        }

        var depreciation = Cell(row, Depreciation) switch
        {
            null => stored?.Depreciation ?? false,
            var v when string.Equals(v, Yes, StringComparison.OrdinalIgnoreCase) => true,
            var v when string.Equals(v, No, StringComparison.OrdinalIgnoreCase) => false,
            _ => throw ValidationException.For(Depreciation, $"Takes {Yes} or {No}."),
        };
        var names = new TranslatedText { Values = new(Names(row, stored?.Name?.Values.ToDictionary(v => v.Key, v => v.Value), language), StringComparer.OrdinalIgnoreCase) };
        if (stored is null)
        {
            db.Add(new VatGroup { Code = Cell(row, Code), Name = names, Rate = rate, Depreciation = depreciation, TenantId = request.TenantId, OwnerId = await visibility.CompanyAsync(ct).ConfigureAwait(false) });
        }
        else
        {
            (stored.Name, stored.Rate, stored.Depreciation) = (names, rate, depreciation);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stored is null;
    }
}

/// <summary>The VAT groups a document type offers: a file sets exactly the groups of the types it names.</summary>
public sealed class DocumentVatList(ErpDbContext db, RequestContext request, Visibility visibility) : MigrationList
{
    private const string Document = "Document type", Parent = "Document type parent", Group = "VAT group";

    private List<DocumentType> _types = [];
    private List<VatGroup> _groups = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _wanted = [];

    public override async Task<int> CountAsync(CancellationToken ct) => (await LinksAsync(ct).ConfigureAwait(false)).Count;

    private async Task<List<InterDocumentTypeVatGroup>> LinksAsync(CancellationToken ct)
    {
        var ids = (await VatLookups.DocumentTypesAsync(db, visibility, ct).ConfigureAwait(false)).Select(t => (Guid?)t.Id).ToList();
        return await db.Set<InterDocumentTypeVatGroup>().AsNoTracking().Where(l => ids.Contains(l.DocumentTypeId)).ToListAsync(ct).ConfigureAwait(false);
    }

    public override async Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct) =>
    [
        new(Document, true, "The document type's code."), new(Parent, false, "The code of the type it sits under, when two types share a code."),
        new(Group, true, Allowed((await VatLookups.GroupsAsync(db, visibility, ct).ConfigureAwait(false)).Select(VatLookups.NameOf))),
    ];

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        await PrepareAsync(ct).ConfigureAwait(false);
        var types = _types.ToDictionary(t => t.Id);
        var groups = _groups.ToDictionary(g => g.Id);
        return [.. (await LinksAsync(ct).ConfigureAwait(false)).Where(l => l.DocumentTypeId is not null && l.VatGroupId is not null).OrderBy(l => types[l.DocumentTypeId!.Value].Code, StringComparer.Ordinal).Select(l =>
        {
            var type = types[l.DocumentTypeId!.Value];
            return new Dictionary<string, object?>
            {
                [Document] = type.Code, [Parent] = type.IdSup is { } up ? types.GetValueOrDefault(up)?.Code : null, [Group] = groups.TryGetValue(l.VatGroupId!.Value, out var g) ? VatLookups.NameOf(g) : null,
            };
        })];
    }

    public override async Task PrepareAsync(CancellationToken ct)
    {
        _types = await VatLookups.DocumentTypesAsync(db, visibility, ct).ConfigureAwait(false);
        _groups = await VatLookups.GroupsAsync(db, visibility, ct).ConfigureAwait(false);
    }

    public override Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var type = VatLookups.Find(_types, Needed(row, Document), Cell(row, Parent)) ?? throw ValidationException.For(Document, $"There is no document type {Cell(row, Document)}.");
        var group = _groups.FirstOrDefault(g => string.Equals(VatLookups.NameOf(g), Needed(row, Group), StringComparison.OrdinalIgnoreCase)) ?? throw ValidationException.For(Group, $"There is no VAT group {Cell(row, Group)}.");
        if (!_wanted.TryGetValue(type.Id, out var groups))
        {
            _wanted[type.Id] = groups = [];
        }

        if (!groups.Add(group.Id))
        {
            throw ValidationException.For(Group, "appears twice in the file for this document type.");
        }

        return Task.FromResult(false);
    }

    public override async Task FinishAsync(CancellationToken ct)
    {
        foreach (var (type, groups) in _wanted)
        {
            var stored = await db.Set<InterDocumentTypeVatGroup>().Where(l => l.DocumentTypeId == type).ToListAsync(ct).ConfigureAwait(false);
            db.RemoveRange(stored.Where(l => l.VatGroupId is not { } g || !groups.Contains(g)));
            var owner = _types.First(t => t.Id == type).OwnerId;
            db.AddRange(groups.Where(g => stored.All(l => l.VatGroupId != g)).Select(g => new InterDocumentTypeVatGroup { DocumentTypeId = type, VatGroupId = g, TenantId = request.TenantId, OwnerId = owner }));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>The boxes of the VAT return.</summary>
public sealed class VatBoxList(ErpDbContext db, RequestContext request, Visibility visibility, string language) : MigrationList
{
    private const string Code = nameof(Code), Name = nameof(Name), Side = nameof(Side), Amount = nameof(Amount), Order = nameof(Order);

    private const string Sales = nameof(Sales), Purchases = nameof(Purchases), Taxable = "Taxable value", Vat = "VAT";

    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    private async Task<List<VatReturnBox>> BoxesAsync(CancellationToken ct, bool tracked = false)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var rows = db.Set<VatReturnBox>().Where(b => b.OwnerId != null && nodes.Contains(b.OwnerId.Value));
        return await (tracked ? rows : rows.AsNoTracking()).OrderBy(b => b.SortOrder).ToListAsync(ct).ConfigureAwait(false);
    }

    public override async Task<int> CountAsync(CancellationToken ct) => (await BoxesAsync(ct).ConfigureAwait(false)).Count;

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        var columns = new List<MigrationColumn> { new(Code, true, "The box's number on the form."), new(Name, true, "The name in English."), new(Side, true, $"{Sales} or {Purchases}."), new(Amount, true, $"{Taxable} or {Vat}."), new(Order, false, "Its place on the form, a number.") };
        if (LocalHeader(language) is { } local)
        {
            columns.Insert(2, new(local, true, $"The name in the company's language ({language})."));
        }

        return Task.FromResult<IReadOnlyList<MigrationColumn>>(columns);
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct) =>
        [.. (await BoxesAsync(ct).ConfigureAwait(false)).Select(b =>
        {
            var row = new Dictionary<string, object?> { [Code] = b.Code, [Name] = b.Name?.In(TranslatedText.DefaultLanguage), [Side] = b.IsInput ? Purchases : Sales, [Amount] = b.Amount == 2 ? Vat : Taxable, [Order] = (decimal)b.SortOrder };
            if (LocalHeader(language) is { } local)
            {
                row[local] = b.Name?.Values.GetValueOrDefault(language);
            }

            return row;
        })];

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var code = Needed(row, Code);
        if (!_seen.Add(code))
        {
            throw ValidationException.For(Code, "appears twice in the file.");
        }

        var side = Needed(row, Side);
        if (!string.Equals(side, Sales, StringComparison.OrdinalIgnoreCase) && !string.Equals(side, Purchases, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationException.For(Side, $"Takes {Sales} or {Purchases}.");
        }

        var amount = Needed(row, Amount);
        if (!string.Equals(amount, Taxable, StringComparison.OrdinalIgnoreCase) && !string.Equals(amount, Vat, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationException.For(Amount, $"Takes {Taxable} or {Vat}.");
        }

        var stored = (await BoxesAsync(ct, tracked: true).ConfigureAwait(false)).FirstOrDefault(b => string.Equals(b.Code, code, StringComparison.OrdinalIgnoreCase));
        var names = new TranslatedText { Values = new(Names(row, stored?.Name?.Values.ToDictionary(v => v.Key, v => v.Value), language), StringComparer.OrdinalIgnoreCase) };
        var box = stored ?? new VatReturnBox { Code = code, TenantId = request.TenantId, OwnerId = await visibility.CompanyAsync(ct).ConfigureAwait(false) };
        (box.Name, box.IsInput, box.Amount) = (names, string.Equals(side, Purchases, StringComparison.OrdinalIgnoreCase), string.Equals(amount, Vat, StringComparison.OrdinalIgnoreCase) ? 2 : 1);
        box.SortOrder = Cell(row, Order) is null ? box.SortOrder : (double)Number(row, Order);
        if (stored is null)
        {
            db.Add(box);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stored is null;
    }
}

/// <summary>What goes into each box: a file sets exactly the rules of the boxes it names, and an empty column on a rule means any.</summary>
public sealed class VatRuleList(ErpDbContext db, RequestContext request, Visibility visibility) : MigrationList
{
    private const string Box = nameof(Box), Category = "Document category", Document = "Document type", Parent = "Document type parent", Line = "Transaction type", Group = "VAT group";

    private sealed record Wanted(Guid? Category, Guid? Document, Guid? Line, Guid? Group);

    private List<VatReturnBox> _boxes = [];
    private List<DocumentCategory> _categories = [];
    private List<DocumentType> _types = [];
    private List<TransactionType> _lines = [];
    private List<VatGroup> _groups = [];
    private readonly Dictionary<Guid, HashSet<Wanted>> _wanted = [];

    public override async Task<int> CountAsync(CancellationToken ct)
    {
        await PrepareAsync(ct).ConfigureAwait(false);
        var ids = _boxes.Select(b => b.Id).ToList();
        return await db.Set<VatReturnBoxRule>().AsNoTracking().CountAsync(r => ids.Contains(r.BoxId), ct).ConfigureAwait(false);
    }

    public override async Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        await PrepareAsync(ct).ConfigureAwait(false);
        return
        [
            new(Box, true, "The box's number."), new(Category, false, $"{Allowed(_categories.Select(c => c.Code))}. Empty: any."), new(Document, false, "The document type's code. Empty: any."),
            new(Parent, false, "The code of the type it sits under, when two types share a code."), new(Line, false, "The transaction type's code. Empty: any."), new(Group, false, $"{Allowed(_groups.Select(VatLookups.NameOf))}. Empty: any."),
        ];
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        await PrepareAsync(ct).ConfigureAwait(false);
        var ids = _boxes.Select(b => b.Id).ToList();
        var rules = await db.Set<VatReturnBoxRule>().AsNoTracking().Where(r => ids.Contains(r.BoxId)).ToListAsync(ct).ConfigureAwait(false);
        return [.. rules.Select(r => (Rule: r, Box: _boxes.First(b => b.Id == r.BoxId))).OrderBy(x => x.Box.SortOrder).Select(x =>
        {
            var type = _types.FirstOrDefault(t => t.Id == x.Rule.DocumentTypeId);
            return new Dictionary<string, object?>
            {
                [Box] = x.Box.Code, [Category] = _categories.FirstOrDefault(c => c.Id == x.Rule.DocumentCategoryId)?.Code, [Document] = type?.Code,
                [Parent] = type?.IdSup is { } up ? _types.FirstOrDefault(t => t.Id == up)?.Code : null, [Line] = _lines.FirstOrDefault(l => l.Id == x.Rule.TransactionTypeId)?.Code,
                [Group] = _groups.FirstOrDefault(g => g.Id == x.Rule.VatGroupId) is { } group ? VatLookups.NameOf(group) : null,
            };
        })];
    }

    public override async Task PrepareAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        _boxes = await db.Set<VatReturnBox>().AsNoTracking().Where(b => b.OwnerId != null && nodes.Contains(b.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false);
        _categories = await db.Set<DocumentCategory>().AsNoTracking().Where(c => c.OwnerId == null || nodes.Contains(c.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false);
        _types = await VatLookups.DocumentTypesAsync(db, visibility, ct).ConfigureAwait(false);
        _lines = await db.Set<TransactionType>().AsNoTracking().Where(t => t.OwnerId == null || nodes.Contains(t.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false);
        _groups = await VatLookups.GroupsAsync(db, visibility, ct).ConfigureAwait(false);
    }

    public override Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var box = _boxes.FirstOrDefault(b => string.Equals(b.Code, Needed(row, Box), StringComparison.OrdinalIgnoreCase)) ?? throw ValidationException.For(Box, $"There is no box {Cell(row, Box)}.");
        Guid? category = Cell(row, Category) is { } c ? _categories.FirstOrDefault(x => string.Equals(x.Code, c, StringComparison.OrdinalIgnoreCase))?.Id ?? throw ValidationException.For(Category, $"There is no category {c}.") : null;
        Guid? document = Cell(row, Document) is { } d ? VatLookups.Find(_types, d, Cell(row, Parent))?.Id ?? throw ValidationException.For(Document, $"There is no document type {d}.") : null;
        Guid? line = Cell(row, Line) is { } l ? _lines.FirstOrDefault(x => string.Equals(x.Code, l, StringComparison.OrdinalIgnoreCase))?.Id ?? throw ValidationException.For(Line, $"There is no transaction type {l}.") : null;
        Guid? group = Cell(row, Group) is { } g ? _groups.FirstOrDefault(x => string.Equals(VatLookups.NameOf(x), g, StringComparison.OrdinalIgnoreCase))?.Id ?? throw ValidationException.For(Group, $"There is no VAT group {g}.") : null;
        if (!_wanted.TryGetValue(box.Id, out var rules))
        {
            _wanted[box.Id] = rules = [];
        }

        if (!rules.Add(new(category, document, line, group)))
        {
            throw ValidationException.For(Box, "This rule appears twice in the file.");
        }

        return Task.FromResult(false);
    }

    public override async Task FinishAsync(CancellationToken ct)
    {
        foreach (var (box, rules) in _wanted)
        {
            var stored = await db.Set<VatReturnBoxRule>().Where(r => r.BoxId == box).ToListAsync(ct).ConfigureAwait(false);
            var kept = stored.Select(r => new Wanted(r.DocumentCategoryId, r.DocumentTypeId, r.TransactionTypeId, r.VatGroupId)).ToHashSet();
            db.RemoveRange(stored.Where(r => !rules.Contains(new(r.DocumentCategoryId, r.DocumentTypeId, r.TransactionTypeId, r.VatGroupId))));
            var owner = _boxes.First(b => b.Id == box).OwnerId;
            db.AddRange(rules.Except(kept).Select(r => new VatReturnBoxRule { BoxId = box, DocumentCategoryId = r.Category, DocumentTypeId = r.Document, TransactionTypeId = r.Line, VatGroupId = r.Group, TenantId = request.TenantId, OwnerId = owner }));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

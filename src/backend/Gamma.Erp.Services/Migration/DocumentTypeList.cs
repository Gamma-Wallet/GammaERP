using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>
/// The types of an entity by code and the type above it, with their numbering; document types (journal types share codes with the top ones) also with their
/// settings. Document sides and VAT groups stay under Document types, transaction scopes under Transaction scopes, so a new type starts here and is finished there.
/// </summary>
public sealed class DocumentTypeList(IEntityRows entity, ErpDbContext db, string language, bool documents = true) : MigrationList
{
    private const string Code = nameof(Code), Name = nameof(Name), Parent = nameof(Parent), Category = nameof(Category), First = "First number", Last = "Last number",
        NeverPosted = "Never posted", PartialReversal = "Partial reversal", Reversal = nameof(Reversal), Reverses = nameof(Reverses), ReversesParent = "Reverses parent";

    private const string Yes = nameof(Yes), No = nameof(No);

    private List<ElementTypeDto> _types = [];
    private List<CategoryDto> _categories = [];
    private readonly HashSet<(string, string?)> _seen = [];

    public override async Task<int> CountAsync(CancellationToken ct) => (await entity.TypesAsync(ct).ConfigureAwait(false)).Count;

    public override async Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        var categories = await entity.CategoriesAsync(ct).ConfigureAwait(false);
        var columns = new List<MigrationColumn>
        {
            new(Code, true, "Rows are matched by code and parent: an existing one is updated."), new(Name, true, "The name in English."), new(Parent, false, "The code of the type it sits under. Empty: the top."),
            new(Category, false, $"One or several, separated by {Separator}: {Allowed(categories.Where(c => c.IsElement).Select(c => c.Code))}"), new(First, false, "The first number of its range."), new(Last, false, "The last number of its range."),
        };
        if (documents)
        {
            columns.AddRange(
            [
                new(NeverPosted, false, $"{Yes} or {No}: a contract or an offer."), new(PartialReversal, false, $"{Yes} or {No}."), new(Reversal, false, $"{Yes} or {No}: it corrects another type."), new(Reverses, false, "The code of the type it corrects."),
                new(ReversesParent, false, "The code of the type that one sits under, when two types share its code. Empty: the top."),
            ]);
        }

        if (LocalHeader(language) is { } local)
        {
            columns.Insert(2, new(local, true, $"The name in the company's language ({language})."));
        }

        return columns;
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        await PrepareAsync(ct).ConfigureAwait(false);
        var ids = _types.Select(t => t.Id!.Value).ToList();
        var stored = !documents ? [] : await db.Set<DocumentType>().AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var codes = _types.ToDictionary(t => t.Id!.Value, t => t.Code);
        var categories = _categories.ToDictionary(c => c.Id!.Value, c => c.Code);
        return [.. _types.OrderBy(t => t.Code, StringComparer.Ordinal).Select(t =>
        {
            var row = new Dictionary<string, object?>
            {
                [Code] = t.Code, [Name] = English(t.Name), [Parent] = t.IdSup is { } up ? codes.GetValueOrDefault(up) : null, [Category] = string.Join(Separator, t.CategoryIds.Select(c => categories.GetValueOrDefault(c))),
                [First] = t.CodeStart, [Last] = t.CodeEnd,
            };
            if (stored.GetValueOrDefault(t.Id!.Value) is { } type)
            {
                (row[NeverPosted], row[PartialReversal], row[Reversal]) = (type.NeverPosted ? Yes : No, type.AllowsPartialReversal ? Yes : No, type.IsReversal ? Yes : No);
                row[Reverses] = type.ReversedDocumentTypeId is { } of ? codes.GetValueOrDefault(of) : null;
                row[ReversesParent] = type.ReversedDocumentTypeId is { } corrected && _types.FirstOrDefault(c => c.Id == corrected)?.IdSup is { } above ? codes.GetValueOrDefault(above) : null;
            }

            if (LocalHeader(language) is { } local)
            {
                row[local] = t.Name.GetValueOrDefault(language);
            }

            return row;
        })];
    }

    public override async Task PrepareAsync(CancellationToken ct)
    {
        _types = [.. await entity.TypesAsync(ct).ConfigureAwait(false)];
        _categories = [.. await entity.CategoriesAsync(ct).ConfigureAwait(false)];
    }

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var code = Needed(row, Code);
        var parentCode = Cell(row, Parent);
        if (!_seen.Add((code.ToUpperInvariant(), parentCode?.ToUpperInvariant())))
        {
            throw ValidationException.For(Code, "appears twice in the file under this parent.");
        }

        var parent = parentCode is null ? null
            : _types.FirstOrDefault(t => t.IdSup is null && string.Equals(t.Code, parentCode, StringComparison.OrdinalIgnoreCase)) ?? throw ValidationException.For(Parent, $"There is no type {parentCode} at the top.");
        var existing = _types.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase) && t.IdSup == parent?.Id);
        var categories = (Cell(row, Category)?.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []).Select(c =>
            _categories.FirstOrDefault(x => string.Equals(x.Code, c, StringComparison.OrdinalIgnoreCase))?.Id ?? throw ValidationException.For(Category, $"There is no category {c}.")).OfType<Guid>().ToList();
        var dto = (existing ?? new ElementTypeDto { IsElement = true }) with
        {
            Code = code, Name = Names(row, existing?.Name, language), IdSup = parent?.Id, Active = true, CategoryIds = categories.Count > 0 ? categories : existing?.CategoryIds ?? [],
            CodeStart = Whole(row, First) ?? existing?.CodeStart, CodeEnd = Whole(row, Last) ?? existing?.CodeEnd,
        };
        var saved = await entity.SaveTypeAsync(dto, ct).ConfigureAwait(false);
        _types.RemoveAll(t => t.Id == saved.Id);
        _types.Add(saved);
        if (!documents)
        {
            return existing is null;
        }

        var stored = await db.Set<DocumentType>().FirstAsync(t => t.Id == saved.Id, ct).ConfigureAwait(false);
        stored.NeverPosted = Flag(row, NeverPosted) ?? stored.NeverPosted;
        stored.AllowsPartialReversal = Flag(row, PartialReversal) ?? stored.AllowsPartialReversal;
        stored.IsReversal = Flag(row, Reversal) ?? stored.IsReversal;
        if (Cell(row, Reverses) is { } reversed)
        {
            var above = Cell(row, ReversesParent);
            var same = _types.Where(t => string.Equals(t.Code, reversed, StringComparison.OrdinalIgnoreCase)).ToList();
            var corrected = same.Count == 1 ? same[0] : same.FirstOrDefault(t => above is null ? t.IdSup is null
                : _types.FirstOrDefault(c => c.Id == t.IdSup) is { } up && string.Equals(up.Code, above, StringComparison.OrdinalIgnoreCase));
            stored.ReversedDocumentTypeId = corrected?.Id ?? throw ValidationException.For(Reverses, $"There is no type {reversed}.");
        }

        if (stored.IsReversal && stored.ReversedDocumentTypeId is null)
        {
            throw ValidationException.For(Reverses, "A reversal type names the one type it corrects.");
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing is null;
    }

    private static int? Whole(IReadOnlyDictionary<string, string> row, string header) => Cell(row, header) is null ? null : (int)Number(row, header);

    private static bool? Flag(IReadOnlyDictionary<string, string> row, string header) => Cell(row, header) switch
    {
        null => null,
        var v when string.Equals(v, Yes, StringComparison.OrdinalIgnoreCase) => true,
        var v when string.Equals(v, No, StringComparison.OrdinalIgnoreCase) => false,
        _ => throw ValidationException.For(header, $"Takes {Yes} or {No}."),
    };
}

using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>Which line types a document type takes, and on a journal which amounts move the line's balance: the link posting groups hang on.</summary>
public sealed class LineKindList(ErpDbContext db, RequestContext request, Visibility visibility) : MigrationList
{
    private const string Document = "Document type", Parent = "Document type parent", Line = "Transaction type", Debit = "Balance debit", Credit = "Balance credit";

    private List<DocumentType> _documents = [];
    private List<TransactionType> _lines = [];
    private Dictionary<string, Elements> _amounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(Guid, Guid)> _seen = [];

    private async Task LoadAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        _documents = await db.Set<DocumentType>().AsNoTracking().Where(t => t.OwnerId != null && nodes.Contains(t.OwnerId.Value)).OrderBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false);
        _lines = await db.Set<TransactionType>().AsNoTracking().Where(t => t.OwnerId == null || nodes.Contains(t.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false);
        var table = db.Model.FindEntityType(typeof(Transaction))!.GetTableName();
        _amounts = new((await db.Set<Elements>().AsNoTracking().Where(e => e.Table == table && e.Record != null).ToListAsync(ct).ConfigureAwait(false)).Where(e => e.Name is not null)
            .GroupBy(e => e.Name!.In(TranslatedText.DefaultLanguage) ?? string.Empty).Select(g => KeyValuePair.Create(g.Key, g.First())), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<InterDocumentTypeTransactionType>> LinksAsync(CancellationToken ct)
    {
        var ids = _documents.Select(d => (Guid?)d.Id).ToList();
        return await db.Set<InterDocumentTypeTransactionType>().AsNoTracking().Where(l => ids.Contains(l.DocumentTypeId)).ToListAsync(ct).ConfigureAwait(false);
    }

    public override async Task<int> CountAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(false);
        return (await LinksAsync(ct).ConfigureAwait(false)).Count;
    }

    public override async Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(false);
        return
        [
            new(Document, true, "The document type's code."), new(Parent, false, "The code of the type it sits under, when two types share a code."), new(Line, true, Allowed(_lines.Select(l => l.Code))),
            new(Debit, false, $"On a journal, the amount that goes on the debit of the line's balance: {Allowed(_amounts.Keys)}"), new(Credit, false, "The same, on the credit."),
        ];
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(false);
        var codes = _documents.ToDictionary(d => d.Id, d => d);
        var lines = _lines.ToDictionary(l => l.Id, l => l.Code);
        var amounts = _amounts.ToDictionary(a => a.Value.Id, a => a.Key);
        return [.. (await LinksAsync(ct).ConfigureAwait(false)).Select(l => (Link: l, Type: codes[l.DocumentTypeId!.Value])).OrderBy(x => x.Type.Code, StringComparer.Ordinal).Select(x => new Dictionary<string, object?>
        {
            [Document] = x.Type.Code, [Parent] = x.Type.IdSup is { } up ? codes.GetValueOrDefault(up)?.Code : null, [Line] = x.Link.TransactionTypeId is { } t ? lines.GetValueOrDefault(t) : null,
            [Debit] = x.Link.ElementDebitValueElementsId is { } d ? amounts.GetValueOrDefault(d) : null, [Credit] = x.Link.ElementCreditValueElementsId is { } c ? amounts.GetValueOrDefault(c) : null,
        })];
    }

    public override Task PrepareAsync(CancellationToken ct) => LoadAsync(ct);

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var code = Needed(row, Document);
        var parent = Cell(row, Parent);
        var document = _documents.Where(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase)).FirstOrDefault(d => parent is null ? d.IdSup is null || _documents.Count(o => string.Equals(o.Code, code, StringComparison.OrdinalIgnoreCase)) == 1
            : _documents.FirstOrDefault(o => o.Id == d.IdSup) is { } up && string.Equals(up.Code, parent, StringComparison.OrdinalIgnoreCase))
            ?? throw ValidationException.For(Document, $"There is no document type {code}.");
        var line = _lines.FirstOrDefault(l => string.Equals(l.Code, Needed(row, Line), StringComparison.OrdinalIgnoreCase)) ?? throw ValidationException.For(Line, $"There is no transaction type {Cell(row, Line)}.");
        if (!_seen.Add((document.Id, line.Id)))
        {
            throw ValidationException.For(Line, "appears twice in the file for this document type.");
        }

        Guid? Amount(string header) => Cell(row, header) is { } name ? _amounts.GetValueOrDefault(name)?.Id ?? throw ValidationException.For(header, $"Takes {Allowed(_amounts.Keys)}.") : null;
        var (debit, credit) = (Amount(Debit), Amount(Credit));
        var stored = await db.Set<InterDocumentTypeTransactionType>().FirstOrDefaultAsync(l => l.DocumentTypeId == document.Id && l.TransactionTypeId == line.Id, ct).ConfigureAwait(false);
        if (stored is null)
        {
            db.Add(new InterDocumentTypeTransactionType { DocumentTypeId = document.Id, TransactionTypeId = line.Id, ElementDebitValueElementsId = debit, ElementCreditValueElementsId = credit, TenantId = request.TenantId, OwnerId = document.OwnerId });
        }
        else if (stored.ElementDebitValueElementsId != debit || stored.ElementCreditValueElementsId != credit)
        {
            (stored.ElementDebitValueElementsId, stored.ElementCreditValueElementsId) = (debit, credit);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stored is null;
    }
}

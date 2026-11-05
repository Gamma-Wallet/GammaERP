using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>A document type a document may make, and why not now when it may not.</summary>
public sealed record DocumentFlowDto(Guid TypeId, string? Code, string? Name, Guid? CategoryId, string? Refused);

/// <summary>A document made from this one.</summary>
public sealed record MadeDocumentDto(Guid Id, string? Code, string? Type, Guid? CategoryId, bool Posted);

/// <summary>
/// Offer to contract to invoice (the founder, 2026-11-01): a document makes a document of a type its flow names (<see
/// cref="DocumentTypeFlow"/>), an unposted copy of its lines, linked to it, for the person to check and post. Whether it may be done only
/// once and until which date are the flow's.
/// </summary>
public sealed partial class DocumentService
{
    /// <summary>The types this document may make, those it may not make now with the reason.</summary>
    public async Task<IReadOnlyList<DocumentFlowDto>> FlowsAsync(Guid id, string language, CancellationToken ct)
    {
        var source = await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false);
        if (source is null || await Entities.ElementAsync(id, ct).ConfigureAwait(false) is null)
        {
            return [];
        }

        var rows = await (from f in db.Set<DocumentTypeFlow>().AsNoTracking() join t in db.Set<DocumentType>().AsNoTracking() on f.TargetTypeId equals t.Id
                          where f.SourceTypeId == source.TypeId && !(f.TargetTypeId == source.TypeId && t.BillsByPlan) orderby t.SortOrder select new { Flow = f, Type = t })
            .ToListAsync(ct).ConfigureAwait(false);
        var flows = new List<DocumentFlowDto>();
        foreach (var row in rows)
        {
            var category = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == row.Type.Id).Select(l => (Guid?)l.CategoryId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            flows.Add(new DocumentFlowDto(row.Type.Id, row.Type.Code, row.Type.Name.In(language), category, await RefusedAsync(source, row.Flow, ct).ConfigureAwait(false)));
        }

        return flows;
    }

    /// <summary>The documents made from this one, the first first.</summary>
    public async Task<IReadOnlyList<MadeDocumentDto>> MadeFromAsync(Guid id, string language, CancellationToken ct)
    {
        var made = await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                          join t in db.Set<DocumentType>().AsNoTracking() on d.TypeId equals t.Id
                          where l.Document1Id == id orderby d.Id select new { d.Id, d.Code, Type = t.Name, TypeId = t.Id, d.Posted }).ToListAsync(ct).ConfigureAwait(false);
        var list = new List<MadeDocumentDto>();
        foreach (var d in made)
        {
            var category = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == d.TypeId).Select(l => (Guid?)l.CategoryId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            list.Add(new MadeDocumentDto(d.Id, d.Code, d.Type.In(language), category, d.Posted));
        }

        return list;
    }

    /// <summary>Makes a document of a type the flow names: its lines, partner and sides copied, dated today, unposted, linked to this one.</summary>
    public async Task<DocumentDto> MakeAsync(Guid id, Guid targetType, CancellationToken ct)
    {
        if (await DocumentAsync(id, ct).ConfigureAwait(false) is not { } source)
        {
            throw ValidationException.For("id", "The document does not exist.");
        }

        var document = await db.Set<Document>().AsNoTracking().FirstAsync(d => d.Id == id, ct).ConfigureAwait(false);
        var flow = await db.Set<DocumentTypeFlow>().AsNoTracking().FirstOrDefaultAsync(f => f.SourceTypeId == document.TypeId && f.TargetTypeId == targetType, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: this document does not make documents of that type.");
        if (await RefusedAsync(document, flow, ct).ConfigureAwait(false) is { } refused)
        {
            throw ValidationException.For("typeId", $"{ErrorCodes.NotAllowedError}: {refused}");
        }

        var day = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == targetType).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var made = await SaveAsync(new DocumentDto
        {
            TypeId = targetType, CategoryIds = categories, DocumentDate = day, PostingDate = day, FromId = source.FromId, FromName = source.FromName,
            ToId = source.ToId, ToName = source.ToName, PaymentMethodId = source.PaymentMethodId, CreatedFromIds = [id], OpportunityId = source.OpportunityId,
            Transactions = [.. source.Transactions.Select(Copied)],
        }, ct).ConfigureAwait(false);

        // An invoice from a contract billed by plan bills its period, whether the run or a person made it: the next date moves, so the period is not billed twice.
        // A renewal (a document of the contract's own type) does not.
        if (targetType != document.TypeId && await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == document.TypeId && t.BillsByPlan, ct).ConfigureAwait(false)
            && await db.Set<ContractBilling>().FirstOrDefaultAsync(b => b.Active && b.ContractId == id, ct).ConfigureAwait(false) is { } plan)
        {
            plan.NextDate = plan.NextDate.AddMonths(plan.EveryMonths);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return made;
    }

    private static TransactionDto Copied(TransactionDto line) => line with { Id = null, IdSup = null, Code = null, DocumentId = null, Posted = false };

    /// <summary>Why the flow can't be taken now: the target has not the same kinds of rows, done already, or past the source's date; null when it can.</summary>
    /// <summary>A contract's end date: its property that the type's flows name as "valid until".</summary>
    private async Task<DateOnly?> EndDateAsync(Guid document, Guid type, CancellationToken ct)
    {
        var info = await db.Set<DocumentTypeFlow>().AsNoTracking().Where(f => f.SourceTypeId == type && f.ValidUntilInfoId != null).Select(f => f.ValidUntilInfoId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var value = info is null ? null : await db.Set<DocumentInfo>().AsNoTracking().Where(v => v.Active && v.ElementId == document && v.TypeInfoId == info)
            .Select(v => v.Value).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return value?.ToString() is { Length: >= 10 } text && DateOnly.TryParse(text[..10], System.Globalization.CultureInfo.InvariantCulture, out var day) ? day : null;
    }

    private async Task<string?> RefusedAsync(Document source, DocumentTypeFlow flow, CancellationToken ct)
    {
        if (await new Approvals(db, request, visibility, clock).RefusalAsync(source, ct).ConfigureAwait(false) is { } waits)
        {
            return waits;
        }

        if (flow.NeedsAcceptance && source.AcceptedOn is null)
        {
            return "the client has not accepted it.";
        }

        // The founder (2026-11-04): an invoice (a type that holds an open item) is made only from a posted offer, contract or order.
        if (!source.Posted && await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == source.TypeId && t.NeverPosted, ct).ConfigureAwait(false)
            && await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == flow.TargetTypeId && t.HoldsOpenItem, ct).ConfigureAwait(false))
        {
            return "post it first: an invoice is made only from a posted document.";
        }

        // The founder (2026-11-01): the other document must hold the same kinds of rows as this one has.
        var kinds = await db.Set<Transaction>().AsNoTracking().Where(t => t.DocumentId == source.Id).Select(t => t.TypeId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var held = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking().Where(l => l.DocumentTypeId == flow.TargetTypeId && l.TransactionTypeId != null)
            .Select(l => l.TransactionTypeId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var missing = kinds.Except(held).ToList();
        if (missing.Count > 0)
        {
            var names = await db.Set<TransactionType>().AsNoTracking().Where(t => missing.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false);
            return $"that document type has no {string.Join(", ", names.Select(t => t.Name.In(TranslatedText.DefaultLanguage)))} rows.";
        }

        if (flow.Once)
        {
            var made = await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                              where l.Document1Id == source.Id && d.TypeId == flow.TargetTypeId && d.Active select d.Id).ToListAsync(ct).ConfigureAwait(false);
            // A plan-billed contract's own type: only a renewal (a copy that ends later) counts as done; a plain copy (Repeat) does not.
            if (made.Count > 0 && flow.TargetTypeId == source.TypeId
                && await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == source.TypeId && t.BillsByPlan, ct).ConfigureAwait(false)
                && await EndDateAsync(source.Id, source.TypeId, ct).ConfigureAwait(false) is { } end)
            {
                var renewed = false;
                foreach (var copy in made)
                {
                    renewed |= await EndDateAsync(copy, source.TypeId, ct).ConfigureAwait(false) is { } later && later > end;
                }

                if (!renewed)
                {
                    made.Clear();
                }
            }

            if (made.Count > 0)
            {
                return "a document of this type was already made from it.";
            }
        }

        if (flow.ValidUntilInfoId is { } info)
        {
            var value = await db.Set<DocumentInfo>().AsNoTracking().Where(v => v.ElementId == source.Id && v.TypeInfoId == info).Select(v => v.Value).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            var text = value?.ToString();
            if (text is { Length: >= 10 } && DateOnly.TryParse(text[..10], System.Globalization.CultureInfo.InvariantCulture, out var until)
                && until < DateOnly.FromDateTime(clock.GetLocalNow().DateTime))
            {
                return $"it ended on {until:dd/MM/yyyy}.";
            }
        }

        return null;
    }
}

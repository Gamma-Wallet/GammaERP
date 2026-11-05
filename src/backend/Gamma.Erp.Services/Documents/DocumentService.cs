using System.Globalization;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Posting;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

using DocumentEntities = EntityService<DocumentCategory, DocumentType, InterDocumentTypeCategory, DocumentTypeInfo, DocumentTypeInfoChoice, Document, DocumentInfo,
    InterDocumentCategory, DocumentAccess>;

/// <summary>
/// Financial documents with their transactions and totals, with these rules:
/// <list type="bullet">
/// <item>A document is an element of the documents entity: its type's tree, its categories, the type's next code; a
/// name and code are unique under the same parent and type. It is made in a company (or one of its cost centres) and starts unposted.</item>
/// <item>A user uses only the document types given to the user or to one of the user's roles.</item>
/// <item>Transactions are never updated: every save switches the document's transactions and totals off and writes them again.
/// Each transaction type, configuration and VAT group must
/// be in the document type's configuration.</item>
/// <item>A line: value = (price − discount per unit) × quantity, VAT = value × the VAT group's rate / 100, value with VAT =
/// value + VAT, discount 0 and quantity 1 by default; each amount in the local currency is the amount × the exchange rate.</item>
/// <item>Posting is final: a posted document and its transactions never change or are switched off; a correction is a new document.</item>
/// <item>A user editing a document holds it: others may read it but not change it until it is released or the hold
/// runs out; holding one releases the user's others.</item>
/// <item>Switching a document off switches off the documents under it and all their transactions.</item>
/// </list>
/// </summary>
/// <summary>What is left to reverse of a line: its quantity and the serial numbers not yet back.</summary>
public sealed record ReversibleLineDto(Guid Id, decimal Left, IReadOnlyList<string> Serials);

/// <summary>A document linked to an opportunity, as its panel lists it.</summary>
public sealed record OpportunityDocumentDto(Guid Id, string? Code, string? Type, DateOnly? Date, bool Posted, Guid? CategoryId);

public sealed partial class DocumentService(ErpDbContext db, RequestContext request, Visibility visibility, TimeProvider clock)
{
    /// <summary>
    /// The company property (on the Company cost-centre type, next to NIPT, address…) that says how many minutes a hold lasts without
    /// being renewed; empty means it lasts until it is released.
    /// </summary>
    public const string HoldTimeoutCode = "HOLD_TIMEOUT";

    private DocumentEntities? _entities;
    private Guid _company;

    // The document a reversal is being made from, while ReverseAsync saves it (the only way a reversal is made).
    private Document? _reversing;

    // The group's sale a company's draft is being made from, while SaveForCompanyAsync saves it: lines may stay open for the company to fill in.
    private Guid? _groupSale;

    // Debit and credit of a sheet's journals so far, while the sheet posts them: each may be one-sided, the sheet balances.
    private (decimal Debit, decimal Credit)? _sheetTotals;

    // The reversal sheet a journal's reversal is being made under, while ReverseSheetAsync makes them: it posts them together.
    private Guid? _reversalUnder;

    /// <summary>The categories, types and lists of documents, as for every entity.</summary>
    public DocumentEntities Entities => _entities ??= new(db, request, visibility) { CodeMakesUnique = true, Fill = FillDocument };

    // ---------- Document types per user ----------

    /// <summary>The active document types the request may use, optionally of one category.</summary>
    public async Task<IReadOnlyList<ElementTypeDto>> AllowedTypesAsync(Guid? categoryId, CancellationToken ct)
    {
        var types = (await Entities.TypesAsync(ct).ConfigureAwait(false)).Where(t => t.Active && (categoryId is null || t.CategoryIds.Contains(categoryId.Value)));
        if (request.IsAdministrator)
        {
            return [.. types];
        }

        var given = await GivenTypesAsync(ct).ConfigureAwait(false);
        return [.. types.Where(t => given.Contains(t.Id!.Value))];
    }

    /// <summary>Gives a document type to exactly these users and roles; for administrators only.</summary>
    public async Task SaveTypeUsersAsync(Guid typeId, IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentNullException.ThrowIfNull(roleIds);
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", "Only an administrator gives document types to users.");
        }

        var type = (await Entities.TypesAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => t.Id == typeId)
            ?? throw ValidationException.For("typeId", "The document type does not exist.");
        var links = await db.Set<InterDocumentTypeUser>().Where(l => l.DocumentTypeId == typeId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var gone in links.Where(l => (l.UserId is { } u && !userIds.Contains(u)) || (l.RoleId is { } r && !roleIds.Contains(r))))
        {
            gone.Active = false;
        }

        foreach (var user in userIds.Distinct().Where(u => links.All(l => l.UserId != u)))
        {
            db.Add(new InterDocumentTypeUser { DocumentTypeId = typeId, UserId = user, OwnerId = type.OwnerId });
        }

        foreach (var role in roleIds.Distinct().Where(r => links.All(l => l.RoleId != r)))
        {
            db.Add(new InterDocumentTypeUser { DocumentTypeId = typeId, RoleId = role, OwnerId = type.OwnerId });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ---------- Documents ----------

    /// <summary>One document with its transactions in order, its totals and the documents it was created from; null when not visible.</summary>
    public async Task<DocumentDto?> DocumentAsync(Guid id, CancellationToken ct)
    {
        if (await Entities.ElementAsync(id, ct).ConfigureAwait(false) is not { } element)
        {
            return null;
        }

        var document = await db.Set<Document>().AsNoTracking().FirstAsync(d => d.Id == id, ct).ConfigureAwait(false);
        var transactions = await db.Set<Transaction>().AsNoTracking().Where(t => t.DocumentId == id).ToListAsync(ct).ConfigureAwait(false);
        var totals = await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.DocumentId == id).ToListAsync(ct).ConfigureAwait(false);
        var from = await db.Set<InterDocumentDocument>().AsNoTracking().Where(l => l.Document2Id == id && l.Document1Id != null)
            .Select(l => l.Document1Id!.Value).ToListAsync(ct).ConfigureAwait(false);
        var serials = (await db.Set<TransactionSerial>().AsNoTracking().Where(s => transactions.Select(t => t.Id).Contains(s.TransactionId)).OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(s => s.TransactionId);
        var lineProducts = transactions.Select(t => t.ElementId).OfType<Guid>().Distinct().ToList();
        var tracked = (await (from p in db.Set<Product>().AsNoTracking() join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                              where lineProducts.Contains(p.Id) && k.TracksSerial select p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var withLots = (await (from p in db.Set<Product>().AsNoTracking() join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                               where lineProducts.Contains(p.Id) && k.TracksLot select p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        return new DocumentDto
        {
            Id = element.Id, OwnerId = element.OwnerId, IdSup = element.IdSup, Active = element.Active, CreatedOn = element.CreatedOn, CreatedBy = element.CreatedBy,
            TypeId = element.TypeId, Code = element.Code, Name = element.Name, Description = element.Description, IsComposite = element.IsComposite,
            CategoryIds = element.CategoryIds, Values = element.Values, AccessUserIds = element.AccessUserIds, AccessRoleIds = element.AccessRoleIds,
            CompanyId = document.CompanyId, PostingDate = document.PostingDate, DocumentDate = document.DocumentDate, FromId = document.FromId,
            FromName = document.FromName, ToId = document.ToId, ToName = document.ToName, Posted = document.Posted, ExternalNumber = document.ExternalNumber, DueDate = document.DueDate, OpportunityId = document.OpportunityId,
            PaymentMethodId = document.PaymentMethodId, TableId = document.TableId,
            Transactions = [.. transactions.OrderBy(Line).Select(t => ToDto(t) with { TracksSerial = t.ElementId is { } product && tracked.Contains(product), TracksLot = t.ElementId is { } lotted && withLots.Contains(lotted),
                Serials = [.. serials[t.Id].Select(s => new TransactionSerialDto(s.Serial, s.PartNumber))] })], CreatedFromIds = from, Totals = [.. totals.Select(ToDto)],
            ReversedDocumentId = document.ReversedDocumentId, ReversedDocumentCode = document.ReversedDocumentCode,
        };
    }

    /// <summary>
    /// Creates or changes a document with its transactions. Everything is checked first, and the whole save is one database transaction,
    /// so a refused save leaves nothing behind.
    /// </summary>
    public async Task<DocumentDto> SaveAsync(DocumentDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        // An empty line (null in the list) is refused like any other wrong line, not left to fail further on.
        dto = dto with { Transactions = dto.Transactions ?? [] };
        var empty = dto.Transactions.Select((line, i) => (line, i)).Where(x => x.line is null).ToDictionary(x => $"transactions[{x.i}]", _ => "The line is empty.");
        if (empty.Count > 0)
        {
            throw new ValidationException(empty);
        }

        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("ownerId", "Documents are made in a company or one of its cost centres, not at the corporate.");
        var type = await AllowedTypeAsync(dto.TypeId, ct).ConfigureAwait(false);
        // A reversal is made only by reversing the document it reverses (the founder's rule, 2026-10-30): a document of a reversal type is
        // never saved on its own, nor changed afterwards.
        if (_reversing is null && await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == dto.TypeId && t.IsReversal, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("typeId", $"{ErrorCodes.ReversalError}: a reversal is made only by reversing the document it reverses.");
        }

        // A payment method is one the document type allows (when it names any).
        if (dto.PaymentMethodId is { } method)
        {
            var allowed = await db.Set<Models.Payments.InterPaymentMethodDocumentType>().AsNoTracking().Where(l => l.DocumentTypeId == dto.TypeId && l.PaymentMethodId != null)
                .Select(l => l.PaymentMethodId!.Value).ToListAsync(ct).ConfigureAwait(false);
            if (!await db.Set<Models.Payments.PaymentMethod>().AnyAsync(p => p.Id == method, ct).ConfigureAwait(false) || (allowed.Count > 0 && !allowed.Contains(method)))
            {
                throw ValidationException.For("paymentMethodId", $"{ErrorCodes.NotConfiguredError}: the document type does not take this payment method.");
            }
        }

        if (dto.Id is { } id)
        {
            var existing = await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false)
                ?? throw ValidationException.For("id", "The document does not exist.");
            await CheckOpenAsync(existing, ct).ConfigureAwait(false);
        }

        var lines = await LinesAsync(type.Id!.Value, dto, ct).ConfigureAwait(false);
        // An order at a point of sale that needs a shift is refused without one: the order is never posted, so its save is where that is checked.
        if (await db.Set<Models.Products.PointOfSaleSetting>().AsNoTracking().AnyAsync(s => s.Active && s.OrderTypeId == type.Id, ct).ConfigureAwait(false)
            && await new Pos.PosShifts(db, request, visibility, clock).RefusalAsync(new Document { TypeId = type.Id!.Value, FromId = dto.FromId, CompanyId = company }, lines, ct).ConfigureAwait(false) is { } noShift)
        {
            throw ValidationException.For("shift", noShift);
        }

        // The opportunity a document is for is one of the company's.
        if (dto.OpportunityId is { } opportunity)
        {
            var seenNodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
            if (!await db.Set<Gamma.Erp.Models.Opportunities.Opportunity>().AsNoTracking()
                    .AnyAsync(o => o.Id == opportunity && o.Active && (o.OwnerId == null || seenNodes.Contains(o.OwnerId.Value)), ct).ConfigureAwait(false))
            {
                throw ValidationException.For("opportunityId", "The opportunity does not exist in this company.");
            }
        }

        // A line can't take more than its warehouse has (the founder's rule): refused already on saving, with the places that have it. A type that is
        // never posted (an offer, an order, a contract) moves nothing, so it is not checked.
        if (!await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == type.Id && t.NeverPosted, ct).ConfigureAwait(false))
        {
            await new MovementService(db).PlanAsync(new Document { TypeId = type.Id!.Value, FromId = dto.FromId, ToId = dto.ToId, CompanyId = company, OwnerId = request.NodeId },
                lines, ct).ConfigureAwait(false);
        }
        foreach (var source in dto.CreatedFromIds)
        {
            if (source == dto.Id || await Entities.ElementAsync(source, ct).ConfigureAwait(false) is null)
            {
                throw ValidationException.For("createdFromIds", "A document it was created from does not exist.");
            }
        }

        // A line may point at the line it comes from (id_sup, e.g. the sold line a reversal reverses), only one of a document it was made from.
        var sourceLines = dto.Transactions.Where(t => t.IdSup is not null).Select(t => t.IdSup!.Value).ToList();
        if (sourceLines.Count > 0)
        {
            var sources = dto.CreatedFromIds.ToList();
            var known = await db.Set<Transaction>().AsNoTracking().Where(t => sourceLines.Contains(t.Id) && t.DocumentId != null && sources.Contains(t.DocumentId.Value))
                .Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
            if (sourceLines.Except(known).Any())
            {
                throw ValidationException.For("transactions", "A line can only come from a line of a document this one was created from.");
            }
        }

        _company = company;
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var named = dto.Name.Values.Any(v => !string.IsNullOrWhiteSpace(v)) ? dto : dto with { Name = type.Name };
        var saved = await Entities.SaveElementAsync(named, ct).ConfigureAwait(false);
        var document = await db.Set<Document>().FirstAsync(d => d.Id == saved.Id, ct).ConfigureAwait(false);
        await SwitchOffLinesAsync(document.Id, ct).ConfigureAwait(false);
        foreach (var (line, i) in lines.Select((l, i) => (l, i)))
        {
            (line.DocumentId, line.CompanyId, line.OwnerId, line.Code) = (document.Id, document.CompanyId, document.OwnerId, (i + 1).ToString(CultureInfo.InvariantCulture));
            db.Add(line);
            db.AddRange(dto.Transactions[i].Serials.Where(s => !string.IsNullOrWhiteSpace(s.Serial)).Select(s => new TransactionSerial
            {
                TransactionId = line.Id, Serial = s.Serial.Trim(), PartNumber = string.IsNullOrWhiteSpace(s.PartNumber) ? null : s.PartNumber.Trim(), OwnerId = document.OwnerId,
            }));
        }

        foreach (var total in Totals(lines))
        {
            (total.DocumentId, total.CompanyId, total.OwnerId) = (document.Id, document.CompanyId, document.OwnerId);
            db.Add(total);
        }

        var links = await db.Set<InterDocumentDocument>().Where(l => l.Document2Id == document.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var gone in links.Where(l => l.Document1Id is not { } s || !dto.CreatedFromIds.Contains(s)))
        {
            gone.Active = false;
        }

        foreach (var source in dto.CreatedFromIds.Distinct().Where(s => links.All(l => l.Document1Id != s)))
        {
            db.Add(new InterDocumentDocument { Document1Id = source, Document2Id = document.Id, OwnerId = document.OwnerId });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        return (await DocumentAsync(document.Id, ct).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Saves the purchase a group's sale makes in this company (<see cref="GroupPurchaseService"/>): what the company has no match for stays
    /// empty on the lines, and posting checks them again.
    /// </summary>
    internal async Task<DocumentDto> SaveForCompanyAsync(DocumentDto dto, Guid groupSale, CancellationToken ct)
    {
        _groupSale = groupSale;
        try
        {
            return await SaveAsync(dto, ct).ConfigureAwait(false);
        }
        finally
        {
            _groupSale = null;
        }
    }

    /// <summary>Switches a document off with the documents under it and all their transactions and totals.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var document = await WritableAsync(id, ct).ConfigureAwait(false);
        if (document.Posted && await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == document.TypeId && t.NeverPosted, ct).ConfigureAwait(false))
        {
            // It booked nothing (an offer, a contract, an order), so switching it off cancels it; only another user's hold stops it.
            if (await HolderAsync(document.Id, ct).ConfigureAwait(false) is { } holder && holder != request.UserId)
            {
                throw ValidationException.For("working", $"{ErrorCodes.LockedError}: another user is editing the document.");
            }
        }
        else
        {
            await CheckOpenAsync(document, ct).ConfigureAwait(false);
        }

        if (await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                   where l.Active && l.Document1Id == id && d.Active select d.Id).AnyAsync(ct).ConfigureAwait(false))
        {
            throw ValidationException.For("active", "Documents were made from it: switch those off first.");
        }

        var children = await db.Set<Document>().Where(d => d.IdSup == id).ToListAsync(ct).ConfigureAwait(false);
        if (children.Any(c => c.Posted))
        {
            throw ValidationException.For("posted", $"{ErrorCodes.PostedError}: a document under it is posted.");
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        foreach (var child in children)
        {
            await SwitchOffLinesAsync(child.Id, ct).ConfigureAwait(false);
            child.Active = false;
        }

        await SwitchOffLinesAsync(id, ct).ConfigureAwait(false);
        await ReleaseAsync(id, everyone: true, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await Entities.DeleteElementAsync(id, ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Posts a document: its entries through the scenarios (<see cref="PostingService"/>), then the document and its
    /// transactions marked posted. All or nothing: every entry is worked out and
    /// checked first, and everything is written in one database transaction. From then on the document is final.
    /// </summary>
    private async Task<DocumentDto> PostOneAsync(Guid id, CancellationToken ct)
    {
        var document = await WritableAsync(id, ct).ConfigureAwait(false);
        await CheckOpenAsync(document, ct).ConfigureAwait(false);
        // A type that books nothing (an offer, a contract, an order; the founder, 2026-11-04): posting writes no entry, moves no stock and opens no invoice,
        // it only makes the document final. Its posting date is its document date when none is given.
        var booksNothing = await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == document.TypeId && t.NeverPosted, ct).ConfigureAwait(false);
        if (booksNothing)
        {
            document.PostingDate ??= document.DocumentDate;
        }

        if (document.PostingDate is null)
        {
            throw ValidationException.For("postingDate", "A document is posted on its posting date: it has none.");
        }

        var closed = await db.Set<PostingLock>().AsNoTracking().Where(l => l.OwnerId == document.CompanyId).Select(l => (DateOnly?)l.LockedUntil).MaxAsync(ct).ConfigureAwait(false);
        if (closed is { } until && document.PostingDate <= until)
        {
            throw ValidationException.For("postingDate", $"{ErrorCodes.PeriodClosedError}: the period is closed up to {until:yyyy-MM-dd}.");
        }

        if (booksNothing)
        {
            // A point of sale's order stays as the till uses it (the founder, 2026-11-04): open on its table until it is billed there.
            if (await db.Set<Models.Products.PointOfSaleSetting>().AsNoTracking().AnyAsync(p => p.Active && p.OrderTypeId == document.TypeId, ct).ConfigureAwait(false))
            {
                throw ValidationException.For("typeId", "A point-of-sale order is billed at the till, not posted.");
            }

            if (await new Approvals(db, request, visibility, clock).RefusalAsync(document, ct).ConfigureAwait(false) is { } waiting)
            {
                throw ValidationException.For("approval", $"{ErrorCodes.NotAllowedError}: {waiting}");
            }

            document.Posted = true;
            foreach (var line in await db.Set<Transaction>().Where(t => t.DocumentId == id).ToListAsync(ct).ConfigureAwait(false))
            {
                line.Posted = true;
            }

            await ReleaseAsync(id, everyone: true, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return (await DocumentAsync(id, ct).ConfigureAwait(false))!;
        }

        // Everything is checked again: a saved document may still have lines left open (a purchase made from a sale of the group).
        await LinesAsync(document.TypeId, (await DocumentAsync(id, ct).ConfigureAwait(false))!, ct).ConfigureAwait(false);
        var lines = (await db.Set<Transaction>().Where(t => t.DocumentId == id).ToListAsync(ct).ConfigureAwait(false)).OrderBy(Line).ToList();
        if (lines.Any(l => l.ExchangeRate is null))
        {
            throw ValidationException.For("exchangeRate", "A foreign currency needs its exchange rate: set it on the document and save it.");
        }

        // The founder (2026-11-03): a document over its writer's approval limit is not posted either, not only kept from making the next one.
        if (await new Approvals(db, request, visibility, clock).RefusalAsync(document, ct).ConfigureAwait(false) is { } waits)
        {
            throw ValidationException.For("approval", $"{ErrorCodes.NotAllowedError}: {waits}");
        }

        if (await new Pos.PosShifts(db, request, visibility, clock).RefusalAsync(document, lines, ct).ConfigureAwait(false) is { } noShift)
        {
            throw ValidationException.For("shift", noShift);
        }

        // The movements first: they give each line the stock cost it took (first in, first out), which the entries can read.
        var movements = await new MovementService(db).PostingPlanAsync(document, lines, ct).ConfigureAwait(false);
        var openItems = new OpenItems(db, visibility);
        var kind = await db.Set<DocumentType>().AsNoTracking().FirstAsync(t => t.Id == document.TypeId, ct).ConfigureAwait(false);
        if (kind.PaysOpenItems)
        {
            await openItems.CoverAsync(document, lines, ct).ConfigureAwait(false);
        }

        if (kind.HoldsOpenItem && document.DueDate is null)
        {
            document.DueDate = await openItems.DueDateAsync(document, ct).ConfigureAwait(false);
        }

        var assets = new Depreciation.AssetSale(db);
        await assets.FillAsync(document, lines, ct).ConfigureAwait(false);
        var posting = new PostingService(db);
        var entries = await posting.EntriesAsync(document, lines, ct, balanced: _sheetTotals is null).ConfigureAwait(false);
        if (_sheetTotals is var (sheetDebit, sheetCredit))
        {
            _sheetTotals = (sheetDebit + entries.Sum(e => e.Debit), sheetCredit + entries.Sum(e => e.Credit));
        }


        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        posting.Write(document, entries);
        await movements.ApplyAsync(ct).ConfigureAwait(false);
        await assets.CloseAsync(document, lines, ct).ConfigureAwait(false);
        document.Posted = true;
        foreach (var line in lines)
        {
            line.Posted = true;
        }

        await ReleaseAsync(id, everyone: true, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        // A sale from the group is the company's purchase: made here, so one refused refuses the posting.
        await new GroupPurchaseService(db, request, clock).MakeDraftAsync(document, lines, ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        return (await DocumentAsync(id, ct).ConfigureAwait(false))!;
    }

    // ---------- Reversal: a posted document is never unposted or changed, it is corrected by a linked reversal ----------

    /// <summary>
    /// Reverses a posted document (the founder's rule): a new document of its type's reversal type, a copy with the from and to swapped on
    /// the document and on every line, linked to it (created from it, each line pointing at its original line), posted through its own
    /// configuration — all or nothing. With <paramref name="quantities"/> (line id → quantity) only those lines and quantities, where the type
    /// allows partial reversal; never more than is left after earlier reversals.
    /// </summary>
    public Task<DocumentDto> ReverseAsync(Guid id, IReadOnlyDictionary<Guid, decimal>? quantities, DateOnly postingDate, CancellationToken ct) =>
        ReverseAsync(id, quantities, null, postingDate, ct);

    /// <summary>The reversal, naming per line of a partial reversal which of its serial numbers come back.</summary>
    public async Task<DocumentDto> ReverseAsync(Guid id, IReadOnlyDictionary<Guid, decimal>? quantities, IReadOnlyDictionary<Guid, IReadOnlyList<string>>? serials, DateOnly postingDate, CancellationToken ct)
    {
        await CheckLatestRunAsync(id, ct).ConfigureAwait(false);
        if (_reversalUnder is null && await db.Set<Document>().AsNoTracking().AnyAsync(d => d.IdSup == id, ct).ConfigureAwait(false))
        {
            return await ReverseSheetAsync(id, postingDate, ct).ConfigureAwait(false);
        }

        var original = await WritableAsync(id, ct).ConfigureAwait(false);
        if (!original.Posted)
        {
            throw ValidationException.For("posted", $"{ErrorCodes.ReversalError}: only a posted document is reversed; an unposted one is changed.");
        }

        var type = await db.Set<DocumentType>().AsNoTracking().FirstAsync(t => t.Id == original.TypeId, ct).ConfigureAwait(false);
        // The reversal type that reverses this type (one at most: the database's unique index).
        if (await db.Set<DocumentType>().AsNoTracking().Where(t => t.ReversedDocumentTypeId == type.Id && t.Active && t.IsReversal).Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false) is not { } reversalType)
        {
            throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: no reversal type reverses this document type; set one first.");
        }

        if (quantities is not null && !type.AllowsPartialReversal)
        {
            throw ValidationException.For("quantities", $"{ErrorCodes.ReversalError}: documents of this type are only reversed whole.");
        }

        var lines = (await db.Set<Transaction>().AsNoTracking().Where(t => t.DocumentId == id).ToListAsync(ct).ConfigureAwait(false)).OrderBy(Line).ToList();
        var reversed = await ReversedQuantitiesAsync(lines, ct).ConfigureAwait(false);
        if (quantities?.Keys.Except(lines.Select(l => l.Id)).Any() == true)
        {
            throw ValidationException.For("quantities", $"{ErrorCodes.ReversalError}: a line to reverse is not a line of the document.");
        }

        var copies = new List<TransactionDto>();
        var errors = new Dictionary<string, string>();
        var lineIds = lines.Select(l => l.Id).ToList();
        var numbers = (await db.Set<TransactionSerial>().AsNoTracking().Where(s => lineIds.Contains(s.TransactionId)).OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false)).ToLookup(s => s.TransactionId);
        var back = await (from c in db.Set<Transaction>().AsNoTracking() join s in db.Set<TransactionSerial>().AsNoTracking() on c.Id equals s.TransactionId
                          where c.IdSup != null && lineIds.Contains(c.IdSup.Value) && c.Posted select new { Line = c.IdSup!.Value, s.Serial }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            var left = (line.Quantity ?? 0) - reversed.GetValueOrDefault(line.Id);
            // A line not named takes what is left of it, as the dialog shows it; 0 leaves the line out.
            var take = quantities is not null && quantities.TryGetValue(line.Id, out var typed) ? typed : left;
            if (take <= 0)
            {
                continue;
            }

            if (take > left)
            {
                errors[$"quantities.{line.Id}"] = $"{ErrorCodes.ReversalError}: line {line.Code} has {left.ToString("0.###", CultureInfo.InvariantCulture)} left to reverse.";
                continue;
            }

            // The serial numbers that come back: all that are left, or those named when only some of the line is reversed.
            var open = numbers[line.Id].Where(s => back.All(b => b.Line != line.Id || b.Serial != s.Serial)).ToList();
            var named = serials?.GetValueOrDefault(line.Id);
            var comingBack = named is null ? open : [.. open.Where(s => named.Contains(s.Serial))];
            if (open.Count > 0 && comingBack.Count != take)
            {
                errors[$"serials.{line.Id}"] = $"{ErrorCodes.ReversalError}: line {line.Code} reverses {take.ToString("0.###", CultureInfo.InvariantCulture)}: name that many of its serial numbers.";
                continue;
            }

            var share = line.Quantity is > 0 ? take / line.Quantity.Value : 1;
            copies.Add(new TransactionDto
            {
                Serials = [.. comingBack.Select(s => new TransactionSerialDto(s.Serial, s.PartNumber))],
                IdSup = line.Id, TypeId = line.TypeId, TypeConfId = line.TypeConfId, Name = line.Name.Values, ElementId = line.ElementId, ElementName = line.ElementName,
                ReferenceNumber = line.ReferenceNumber, ReferenceDocumentTypeId = line.ReferenceDocumentTypeId, Lot = line.Lot, LotExpiryDate = line.LotExpiryDate,
                ElementTypeId = line.ElementTypeId, UnitId = line.UnitId, UnitName = line.UnitName, Price = line.Price, Discount = line.Discount, Quantity = take,
                VatGroupId = line.VatGroupId, CurrencyId = line.CurrencyId, ExchangeRate = line.ExchangeRate,
                FromId = line.ToId, FromName = line.ToName, ToId = line.FromId, ToName = line.FromName, Debit = line.Credit * share, Credit = line.Debit * share,
            });
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (copies.Count == 0)
        {
            throw ValidationException.For("quantities", $"{ErrorCodes.ReversalError}: nothing is left to reverse.");
        }

        var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == reversalType).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        DocumentDto reversal;
        _reversing = original;
        try
        {
            reversal = await SaveAsync(new DocumentDto
            {
                TypeId = reversalType, IdSup = _reversalUnder, CategoryIds = categories, DocumentDate = postingDate, PostingDate = postingDate,
                FromId = original.ToId, FromName = original.ToName, ToId = original.FromId, ToName = original.FromName, CreatedFromIds = [original.Id], Transactions = copies,
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _reversing = null;
        }

        await CancelRunAsync(id, ct).ConfigureAwait(false);
        if (_reversalUnder is not null)
        {
            return reversal;
        }

        reversal = await PostAsync(reversal.Id!.Value, ct).ConfigureAwait(false);
        // The company's draft of this sale follows it: what is left of the sale, or nothing.
        var done = await ReversedQuantitiesAsync(lines, ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            line.Quantity = (line.Quantity ?? 0) - done.GetValueOrDefault(line.Id);
        }

        await new GroupPurchaseService(db, request, clock).AfterReversalAsync(original, [.. lines.Where(l => l.Quantity > 0)], ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return reversal;
    }

    /// <summary>What is left to reverse of each line of a posted document the user sees: its quantity and the serial numbers not yet back.</summary>
    public async Task<IReadOnlyList<ReversibleLineDto>> ReversibleAsync(Guid id, CancellationToken ct)
    {
        if (await Entities.ElementAsync(id, ct).ConfigureAwait(false) is null)
        {
            return [];
        }

        var lines = await db.Set<Transaction>().AsNoTracking().Where(t => t.DocumentId == id).ToListAsync(ct).ConfigureAwait(false);
        var reversed = await ReversedQuantitiesAsync(lines, ct).ConfigureAwait(false);
        var lineIds = lines.Select(l => l.Id).ToList();
        var numbers = (await db.Set<TransactionSerial>().AsNoTracking().Where(s => lineIds.Contains(s.TransactionId)).OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(s => s.TransactionId);
        var back = (await (from c in db.Set<Transaction>().AsNoTracking() join s in db.Set<TransactionSerial>().AsNoTracking() on c.Id equals s.TransactionId
                           where c.IdSup != null && lineIds.Contains(c.IdSup.Value) && c.Posted select new { Line = c.IdSup!.Value, s.Serial }).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(b => b.Line, b => b.Serial);
        return [.. lines.Select(l => new ReversibleLineDto(l.Id, (l.Quantity ?? 0) - reversed.GetValueOrDefault(l.Id),
            [.. numbers[l.Id].Select(s => s.Serial).Where(s => !back[l.Id].Contains(s))]))];
    }

    /// <summary>The documents linked to an opportunity, those the user sees (the opportunity's own visibility is checked by the caller).</summary>
    public async Task<IReadOnlyList<OpportunityDocumentDto>> OpportunityDocumentsAsync(Guid opportunityId, string language, CancellationToken ct)
    {
        var rows = await db.Set<Document>().AsNoTracking().Where(d => d.Active && d.OpportunityId == opportunityId).OrderBy(d => d.DocumentDate).ThenBy(d => d.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        var typeIds = rows.Select(d => d.TypeId).Distinct().ToList();
        var types = await db.Set<DocumentType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name.In(language), ct).ConfigureAwait(false);
        var seen = new List<OpportunityDocumentDto>();
        foreach (var row in rows)
        {
            if (await Entities.ElementAsync(row.Id, ct).ConfigureAwait(false) is { } element)
            {
                seen.Add(new OpportunityDocumentDto(row.Id, row.Code, types.GetValueOrDefault(row.TypeId), row.DocumentDate, row.Posted,
                    element.CategoryIds.Select(c => (Guid?)c).FirstOrDefault()));
            }
        }

        return seen;
    }

    /// <summary>How much of each line posted reversals have taken back, by the line's id.</summary>
    private async Task<Dictionary<Guid, decimal>> ReversedQuantitiesAsync(IReadOnlyList<Transaction> lines, CancellationToken ct)
    {
        var lineIds = lines.Select(l => (Guid?)l.Id).ToList();
        return (await (from t in db.Set<Transaction>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on t.DocumentId equals d.Id
                       where lineIds.Contains(t.IdSup) && d.Posted select new { t.IdSup, t.Quantity }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.IdSup!.Value).ToDictionary(g => g.Key, g => g.Sum(t => t.Quantity ?? 0));
    }

    // ---------- Holding a document while editing it ----------

    /// <summary>
    /// The request's user holds the document to edit it: the user's other holds are released, and calling
    /// it again renews the hold. A posted document is final and is not held; one another user holds is refused.
    /// </summary>
    public async Task HoldAsync(Guid id, CancellationToken ct)
    {
        var user = request.UserId ?? throw ValidationException.For("user", "Only a user holds a document.");
        var document = await WritableAsync(id, ct).ConfigureAwait(false);
        await CheckOpenAsync(document, ct).ConfigureAwait(false);
        var rows = await db.Set<DocumentWorking>().Where(w => (w.UserId == user && w.Working) || w.DocumentId == id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var other in rows.Where(w => w.DocumentId != id))
        {
            other.Working = false;
        }

        if (rows.FirstOrDefault(w => w.DocumentId == id) is { } row)
        {
            (row.UserId, row.Working) = (user, true);
            db.Entry(row).Property(r => r.UserId).IsModified = true; // renews the hold (its time) even when nothing else changes
        }
        else
        {
            db.Add(new DocumentWorking { DocumentId = id, CompanyId = document.CompanyId, UserId = user, Working = true, OwnerId = document.OwnerId });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Releases the request's user's hold on a document.</summary>
    public async Task ReleaseAsync(Guid id, CancellationToken ct)
    {
        await ReleaseAsync(id, everyone: false, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Releases every document the request's user holds, e.g. on signing out.</summary>
    public async Task ReleaseAllAsync(CancellationToken ct)
    {
        foreach (var row in await db.Set<DocumentWorking>().Where(w => w.UserId == request.UserId && w.Working).ToListAsync(ct).ConfigureAwait(false))
        {
            row.Working = false;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The user holding the document, or null when no one does. A hold runs out after the company's hold timeout
    /// (<see cref="HoldTimeoutCode"/>); with none set it lasts until it is released.
    /// </summary>
    public async Task<Guid?> HolderAsync(Guid id, CancellationToken ct)
    {
        var holds = db.Set<DocumentWorking>().AsNoTracking().Where(w => w.DocumentId == id && w.Working);
        var company = await db.Set<Document>().AsNoTracking().Where(d => d.Id == id).Select(d => (Guid?)d.CompanyId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is { } c && await HoldTimeoutAsync(c, ct).ConfigureAwait(false) is { } minutes)
        {
            var since = clock.GetUtcNow() - TimeSpan.FromMinutes((double)minutes);
            holds = holds.Where(w => w.CreatedOn > since);
        }

        return await holds.Select(w => w.UserId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The company's hold timeout in minutes: its value of the Company type's <see cref="HoldTimeoutCode"/> property, or null.</summary>
    private async Task<decimal?> HoldTimeoutAsync(Guid company, CancellationToken ct)
    {
        var values = await (from v in db.Set<CostCenterInfo>().AsNoTracking()
                            join p in db.Set<CostCenterTypeInfo>().AsNoTracking() on v.TypeInfoId equals p.Id
                            where v.ElementId == company && p.Kind == PropertyKind.Number && p.Code == HoldTimeoutCode
                            select new { v.Value }).ToListAsync(ct).ConfigureAwait(false);
        var value = values.FirstOrDefault()?.Value;
        return value is null ? null : value.GetValue<decimal>();
    }

    private async Task ReleaseAsync(Guid id, bool everyone, CancellationToken ct)
    {
        foreach (var row in await db.Set<DocumentWorking>().Where(w => w.DocumentId == id && w.Working && (everyone || w.UserId == request.UserId))
            .ToListAsync(ct).ConfigureAwait(false))
        {
            row.Working = false;
        }
    }

    // ---------- Rules ----------

    /// <summary>A document's own columns, set with the element in the same save: a new one is made in the request's company and unposted.</summary>
    private void FillDocument(Document document, ElementDto element, bool isNew)
    {
        if (element is not DocumentDto dto)
        {
            throw ValidationException.For("transactions", "Documents are saved with their transactions, through the document service.");
        }

        if (isNew)
        {
            (document.CompanyId, document.Posted) = (_company, false);
            (document.ReversedDocumentId, document.ReversedDocumentCode) = (_reversing?.Id, _reversing?.Code);
            document.GroupSaleId = _groupSale;
        }

        (document.PostingDate, document.DocumentDate, document.FromId, document.FromName, document.ToId, document.ToName) =
            (dto.PostingDate, dto.DocumentDate, dto.FromId, dto.FromName, dto.ToId, dto.ToName);
        document.ExternalNumber = string.IsNullOrWhiteSpace(dto.ExternalNumber) ? null : dto.ExternalNumber.Trim()[..Math.Min(dto.ExternalNumber.Trim().Length, 64)];
        (document.PaymentMethodId, document.TableId, document.DueDate, document.OpportunityId) = (dto.PaymentMethodId, dto.TableId, dto.DueDate, dto.OpportunityId);
        (document.ApprovedBy, document.ApprovedOn) = (null, null);
        (document.AcceptedBy, document.AcceptedOn, document.RefusedBy, document.RefusedOn) = (null, null, null, null);
    }

    private async Task<HashSet<Guid>> GivenTypesAsync(CancellationToken ct)
    {
        var user = request.UserId;
        var roles = request.RoleIds.ToList();
        return [.. await db.Set<InterDocumentTypeUser>().AsNoTracking()
            .Where(l => l.DocumentTypeId != null && ((l.UserId != null && l.UserId == user) || (l.RoleId != null && roles.Contains(l.RoleId.Value))))
            .Select(l => l.DocumentTypeId!.Value).ToListAsync(ct).ConfigureAwait(false)];
    }

    private async Task<ElementTypeDto> AllowedTypeAsync(Guid typeId, CancellationToken ct)
    {
        var type = (await Entities.TypesAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => t.Id == typeId && t.Active)
            ?? throw ValidationException.For("typeId", "The document type does not exist.");
        if (!request.IsAdministrator && !(await GivenTypesAsync(ct).ConfigureAwait(false)).Contains(typeId))
        {
            throw ValidationException.For("typeId", $"{ErrorCodes.NotAllowedError}: this document type has not been given to you or to one of your roles.");
        }

        return type;
    }

    /// <summary>The document, tracked, when the request sees it and may write it.</summary>
    private async Task<Document> WritableAsync(Guid id, CancellationToken ct)
    {
        if (!await Entities.MayWriteElementAsync(id, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("id", "The document does not exist, or you may not write it.");
        }

        return await db.Set<Document>().FirstAsync(d => d.Id == id, ct).ConfigureAwait(false);
    }

    /// <summary>Refuses a posted document (final) and one another user holds.</summary>
    private async Task CheckOpenAsync(Document document, CancellationToken ct)
    {
        if (document.Posted)
        {
            throw ValidationException.For("posted", $"{ErrorCodes.PostedError}: the document is posted and final; correct it with a new document.");
        }

        if (await HolderAsync(document.Id, ct).ConfigureAwait(false) is { } holder && holder != request.UserId)
        {
            throw ValidationException.For("working", $"{ErrorCodes.LockedError}: another user is editing the document.");
        }
    }

    private async Task SwitchOffLinesAsync(Guid documentId, CancellationToken ct)
    {
        foreach (var line in await db.Set<Transaction>().Where(t => t.DocumentId == documentId).ToListAsync(ct).ConfigureAwait(false))
        {
            line.Active = false;
        }

        foreach (var total in await db.Set<DocumentTotal>().Where(t => t.DocumentId == documentId).ToListAsync(ct).ConfigureAwait(false))
        {
            total.Active = false;
        }
    }

    /// <summary>The document's transactions, checked against the document type's configuration and worked out, not yet added.</summary>
    private async Task<List<Transaction>> LinesAsync(Guid documentTypeId, DocumentDto document, CancellationToken ct)
    {
        var input = document.Transactions;
        // Which of the document type's sides holds a cost centre (warehouse, cash desk…): each line's location is on that side, its own
        // from / to when it has one (a line may take merchandise from another warehouse), otherwise the document's.
        var costCenterTable = db.Model.FindEntityType(typeof(CostCenter))!.GetTableName();
        var fromSide = await (from c in db.Set<DocumentTypeConfFrom>().AsNoTracking() join e in db.Set<Models.Platform.Elements>().AsNoTracking() on c.ElementsId equals e.Id
                              where c.DocumentTypeId == documentTypeId select e.Table).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var toSide = await (from c in db.Set<DocumentTypeConfTo>().AsNoTracking() join e in db.Set<Models.Platform.Elements>().AsNoTracking() on c.ElementsId equals e.Id
                            where c.DocumentTypeId == documentTypeId select e.Table).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Func<TransactionDto, bool>? located = fromSide == costCenterTable ? t => (t.FromId ?? document.FromId) is not null
            : toSide == costCenterTable ? t => (t.ToId ?? document.ToId) is not null : null;
        var transactionTypes = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking()
            .Where(l => l.DocumentTypeId == documentTypeId && l.TransactionTypeId != null).Select(l => l.TransactionTypeId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var typeNames = await db.Set<TransactionType>().AsNoTracking().Where(t => transactionTypes.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct).ConfigureAwait(false);
        var confs = (await db.Set<TransactionTypeConf>().AsNoTracking().Where(c => c.TransactionTypeId != null && transactionTypes.Contains(c.TransactionTypeId.Value))
            .ToListAsync(ct).ConfigureAwait(false)).ToLookup(c => c.TransactionTypeId!.Value, c => c.Id);
        // Each configuration's scope (the elements its lines may carry): a line's element must be one of them, or the document is refused.
        var confIds = confs.SelectMany(g => g).ToList();
        var scopes = await (from c in db.Set<TransactionTypeConf>().AsNoTracking() join e in db.Set<Models.Platform.Elements>().AsNoTracking() on c.ElementsId equals e.Id
                            where confIds.Contains(c.Id) select new { c.Id, Scope = e }).ToDictionaryAsync(x => x.Id, x => x.Scope, ct).ConfigureAwait(false);
        var posting = new Posting.PostingService(db);
        var vatOfType = await db.Set<InterDocumentTypeVatGroup>().AsNoTracking().Where(l => l.DocumentTypeId == documentTypeId && l.VatGroupId != null)
            .Select(l => l.VatGroupId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var vatGroups = await db.Set<VatGroup>().AsNoTracking().ToDictionaryAsync(v => v.Id, ct).ConfigureAwait(false);
        // The document's company's currencies (in a group every company has its own base currency).
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var currencies = await db.Set<Currency>().AsNoTracking().Where(c => c.OwnerId == company || c.OwnerId == null).ToListAsync(ct).ConfigureAwait(false);
        var day = document.DocumentDate ?? document.PostingDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        // How each transaction type's lines are filled in for this document type, with their defaults.
        var fills = await (from l in db.Set<InterDocumentTypeTransactionType>().AsNoTracking()
                           join f in db.Set<TransactionFill>().AsNoTracking() on (Guid?)l.Id equals f.DocumentTypeTransactionTypeId
                           where l.DocumentTypeId == documentTypeId && l.TransactionTypeId != null
                           select new { Type = l.TransactionTypeId!.Value, Fill = f }).ToListAsync(ct).ConfigureAwait(false);
        var fillOf = fills.GroupBy(x => x.Type).ToDictionary(g => g.Key, g => g.First().Fill);
        var unitIds = input.Where(t => t.UnitId is not null && t.UnitName is null).Select(t => t.UnitId!.Value).ToList();
        var units = await db.Set<Unit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct).ConfigureAwait(false);

        var errors = new Dictionary<string, string>();
        var lines = new List<Transaction>();
        foreach (var (dto, i) in input.Select((t, i) => (t, i)))
        {
            var at = $"transactions[{i}]";
            if (!transactionTypes.Contains(dto.TypeId))
            {
                errors[$"{at}.typeId"] = $"{ErrorCodes.NotConfiguredError}: the document type does not have this transaction type.";
                continue;
            }

            var conf = dto.TypeConfId;
            if (conf is { } c ? !confs[dto.TypeId].Contains(c) : confs[dto.TypeId].Count() > 1 && _groupSale is null)
            {
                errors[$"{at}.typeConfId"] = conf is null ? "The transaction type has several configurations: choose one."
                    : $"{ErrorCodes.NotConfiguredError}: the configuration is not one of the transaction type's.";
            }

            conf ??= confs[dto.TypeId].Cast<Guid?>().SingleOrDefault();
            // The founder (2026-10-31): a line whose element its kind does not hold (goods on a services line) refuses the whole document.
            if (conf is { } held && dto.ElementId is { } lineElement && scopes.TryGetValue(held, out var scope)
                && !await posting.HoldsAsync(scope, lineElement, ct).ConfigureAwait(false))
            {
                errors[$"{at}.elementId"] = $"{ErrorCodes.NotConfiguredError}: {dto.ElementName ?? lineElement.ToString()} is not one of the line's "
                    + $"{scope.Name?.In(TranslatedText.DefaultLanguage) ?? "elements"}.";
            }

            decimal rate = 0;
            if (dto.VatGroupId is { } v)
            {
                if (!vatGroups.TryGetValue(v, out var group) || (vatOfType.Count > 0 && !vatOfType.Contains(v)))
                {
                    errors[$"{at}.vatGroupId"] = $"{ErrorCodes.NotConfiguredError}: the VAT group is not one of the document type's.";
                }
                else
                {
                    rate = group.Rate ?? 0;
                }
            }

            var currency = dto.CurrencyId is { } cur ? currencies.FirstOrDefault(x => x.Id == cur) : currencies.FirstOrDefault(x => x.IsBase);
            // A rate not typed is the day's rate (the latest on or before the document's day).
            var exchange = dto.ExchangeRate ?? (currency?.IsBase == true ? 1m
                : currency is not null && currencies.FirstOrDefault(x => x.IsBase) is { } baseCurrency
                    ? await Currencies.CurrencyService.RateOnAsync(db, currency.Id, baseCurrency.Id, day, ct).ConfigureAwait(false) : null);
            if (currency is null)
            {
                errors[$"{at}.currencyId"] = "The currency does not exist.";
            }
            else if (exchange is not > 0)
            {
                errors[$"{at}.exchangeRate"] = "A foreign currency needs its exchange rate.";
            }

            var fill = fillOf.GetValueOrDefault(dto.TypeId);
            var (quantity, discount) = (dto.Quantity ?? fill?.DefaultQuantity, dto.Discount ?? fill?.DefaultDiscount);
            CheckFill(errors, at, i, fill, dto, quantity, discount, located);
            if (_groupSale is not null)
            {
                foreach (var open in (string[])["element", "unit", "location", "vatGroup", "exchangeRate"])
                {
                    errors.Remove($"{at}.{open}");
                }
            }

            decimal price, value, vat;
            if (dto.Price is { } typed)
            {
                price = typed;
                (value, vat) = LineAmounts.Of(typed, discount ?? 0, quantity ?? 0, rate);
            }
            else
            {
                (price, value, vat) = LineAmounts.FromValueWithVat(dto.ValueVat ?? 0, discount ?? 0, quantity ?? 0, rate);
            }

            var x1 = exchange ?? 1;
            var name = dto.Name.Values.Any(n => !string.IsNullOrWhiteSpace(n)) ? new TranslatedText { Values = new(dto.Name, StringComparer.OrdinalIgnoreCase) }
                : dto.ElementName is { Length: > 0 } element ? TranslatedText.English(element) : typeNames[dto.TypeId];
            lines.Add(new Transaction
            {
                IdSup = dto.IdSup,
                TypeId = dto.TypeId, TypeConfId = conf, Name = name, FromId = dto.FromId, FromName = dto.FromName, ToId = dto.ToId, ToName = dto.ToName,
                ElementId = dto.ElementId, ElementName = dto.ElementName, ElementTypeId = dto.ElementTypeId,
                CurrencyId = currency?.Id, CurrencyName = currency?.Name?.In(TranslatedText.DefaultLanguage) ?? currency?.Code, VatGroupId = dto.VatGroupId,
                UnitId = dto.UnitId, UnitName = dto.UnitName ?? (dto.UnitId is { } u && units.TryGetValue(u, out var unit) ? unit.In(TranslatedText.DefaultLanguage) : null),
                Price = price, Discount = discount ?? 0, Quantity = quantity ?? 0, Value = value, Vat = vat, ValueVat = value + vat, ExchangeRate = _groupSale is null ? x1 : exchange,
                DiscountLcu = (discount ?? 0) * x1, ValueLcu = value * x1, VatLcu = vat * x1, ValueVatLcu = (value + vat) * x1, Debit = dto.Debit, Credit = dto.Credit,
                ReferenceNumber = string.IsNullOrWhiteSpace(dto.ReferenceNumber) ? null : dto.ReferenceNumber.Trim(), ReferenceDocumentTypeId = dto.ReferenceDocumentTypeId,
                Lot = string.IsNullOrWhiteSpace(dto.Lot) ? null : dto.Lot.Trim(), LotExpiryDate = dto.LotExpiryDate,
            });
        }

        return errors.Count == 0 ? lines : throw new ValidationException(errors);
    }

    /// <summary>
    /// The line has every field its document type × transaction type needs: 0 typed or chosen, 1 chosen
    /// from the database, 2 not needed. Without a configuration row nothing is required, but no default applies either,
    /// so the quantity and the price (or the value with VAT) must be given. The location is the cost centre side of the
    /// line: its own from / to, or the document's.
    /// </summary>
    private static void CheckFill(Dictionary<string, string> errors, string at, int i, TransactionFill? fill, TransactionDto dto, decimal? quantity, decimal? discount,
        Func<TransactionDto, bool>? located)
    {
        var priced = dto.Price is not null || dto.ValueVat is not null;
        void Need(string field, int? mode, bool chosen, bool typed)
        {
            var missing = mode switch { 1 => !chosen, 0 => !chosen && !typed, _ => false };
            if (missing)
            {
                errors[$"{at}.{field}"] = $"{ErrorCodes.FillError}: line {i + 1} needs its {field}{(mode == 1 ? ", chosen from the list" : string.Empty)}.";
            }
        }

        if (fill is null)
        {
            Need("quantity", 0, quantity is not null, false);
            Need("price", 0, priced, false);
            return;
        }

        Need("vatGroup", fill.VatGroup, dto.VatGroupId is not null, false);
        Need("element", fill.Element, dto.ElementId is not null, !string.IsNullOrWhiteSpace(dto.ElementName));
        Need("unit", fill.Unit, dto.UnitId is not null, !string.IsNullOrWhiteSpace(dto.UnitName));
        if (located is not null)
        {
            Need("location", fill.Location, located(dto), false);
        }

        Need("price", fill.UnitPriceWithoutVat, priced, priced);
        Need("discount", fill.Discount, discount is not null, discount is not null);
        Need("quantity", fill.Quantity, quantity is not null, quantity is not null);
    }

    /// <summary>The totals per currency: Σ discount × quantity, Σ value, Σ VAT, Σ with VAT, × the rate.</summary>
    private static IEnumerable<DocumentTotal> Totals(IEnumerable<Transaction> lines) =>
        lines.GroupBy(l => l.CurrencyId).Select(g =>
        {
            var rate = g.First().ExchangeRate ?? 1;
            var (discount, value, vat) = (g.Sum(l => (l.Discount ?? 0) * (l.Quantity ?? 0)), g.Sum(l => l.Value ?? 0), g.Sum(l => l.Vat ?? 0));
            return new DocumentTotal
            {
                CurrencyId = g.Key, Discount = discount, Value = value, Vat = vat, ValueVat = value + vat, ExchangeRate = rate,
                DiscountLcu = discount * rate, ValueLcu = value * rate, VatLcu = vat * rate, ValueVatLcu = (value + vat) * rate,
            };
        });

    private static int Line(Transaction t) => int.TryParse(t.Code, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;

    private static TransactionDto ToDto(Transaction t) => new()
    {
        Id = t.Id, OwnerId = t.OwnerId, IdSup = t.IdSup, Active = t.Active, CreatedOn = t.CreatedOn, CreatedBy = t.CreatedBy,
        TypeId = t.TypeId, Code = t.Code, Name = t.Name.Values, Description = t.Description?.Values, IsComposite = t.IsComposite,
        CompanyId = t.CompanyId, TypeConfId = t.TypeConfId, DocumentId = t.DocumentId, FromId = t.FromId, FromName = t.FromName, ToId = t.ToId,
        ToName = t.ToName, ElementId = t.ElementId, ElementName = t.ElementName, CurrencyName = t.CurrencyName, CurrencyId = t.CurrencyId,
        VatGroupId = t.VatGroupId, UnitId = t.UnitId, UnitName = t.UnitName, Price = t.Price, Discount = t.Discount, Quantity = t.Quantity,
        Value = t.Value, Vat = t.Vat, ValueVat = t.ValueVat, ExchangeRate = t.ExchangeRate, DiscountLcu = t.DiscountLcu, ValueLcu = t.ValueLcu,
        VatLcu = t.VatLcu, ValueVatLcu = t.ValueVatLcu, CostLcu = t.CostLcu, Posted = t.Posted, ElementTypeId = t.ElementTypeId, Debit = t.Debit, Credit = t.Credit,
        ReferenceNumber = t.ReferenceNumber, ReferenceDocumentTypeId = t.ReferenceDocumentTypeId, Lot = t.Lot, LotExpiryDate = t.LotExpiryDate,
    };

    private static DocumentTotalDto ToDto(DocumentTotal t) => new()
    {
        Id = t.Id, OwnerId = t.OwnerId, Active = t.Active, CreatedOn = t.CreatedOn, CreatedBy = t.CreatedBy, CompanyId = t.CompanyId, DocumentId = t.DocumentId,
        CurrencyId = t.CurrencyId, Discount = t.Discount, Value = t.Value, Vat = t.Vat, ValueVat = t.ValueVat, ExchangeRate = t.ExchangeRate,
        DiscountLcu = t.DiscountLcu, ValueLcu = t.ValueLcu, VatLcu = t.VatLcu, ValueVatLcu = t.ValueVatLcu,
    };
}

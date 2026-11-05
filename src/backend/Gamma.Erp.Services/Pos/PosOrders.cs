using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Pos;

/// <summary>Bills open orders: those named, or a table's.</summary>
public sealed record BillOrdersRequest(Guid BillTypeId, Guid OrderTypeId, Guid? TableId = null, IReadOnlyList<Guid>? OrderIds = null);

/// <summary>
/// Orders at the point of sale (the founder, 2026-11-02): a waiter sends an order, which moves no stock; the bill comes later, at payment, one bill from all the open orders of the table,
/// and takes what was produced from the point of sale. Whether a type makes the other is configuration (tbl_document_type_flow). A bill waits until a production point produced everything
/// routed to it. A cancelled order simply stops: what was produced stays in stock.
/// </summary>
public sealed class PosOrders(ErpDbContext db, Visibility visibility, DocumentService documents, ProductionDisplay display, TimeProvider clock)
{
    /// <summary>The open orders of a type, of one table or all.</summary>
    public async Task<IReadOnlyList<UnpaidBillDto>> OpenAsync(Guid orderType, Guid? table, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        return await PosService.OpenOrdersAsync(db, companies, orderType, table, ct).ConfigureAwait(false);
    }

    /// <summary>One posted bill from the open orders, each of its lines pointing at the order's line.</summary>
    public async Task<DocumentDto> BillAsync(BillOrdersRequest bill, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bill);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("billTypeId", "Bills belong to a company: choose one of its nodes.");
        if (!await db.Set<DocumentTypeFlow>().AsNoTracking().AnyAsync(f => f.SourceTypeId == bill.OrderTypeId && f.TargetTypeId == bill.BillTypeId, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("billTypeId", $"{ErrorCodes.NotConfiguredError}: this order does not make documents of that type.");
        }

        var open = await OpenAsync(bill.OrderTypeId, bill.TableId, ct).ConfigureAwait(false);
        var ids = open.Where(o => bill.OrderIds is not { Count: > 0 } named || named.Contains(o.Id)).Select(o => o.Id).ToList();
        if (ids.Count == 0)
        {
            throw ValidationException.For("orderIds", "There is no open order to bill.");
        }

        if (await display.NotProducedAsync(company, ids, ct).ConfigureAwait(false) is { Count: > 0 } waiting)
        {
            throw ValidationException.For("orderIds", $"{ErrorCodes.NotAllowedError}: not produced yet: {string.Join(", ", waiting)}.");
        }

        var orders = new List<DocumentDto>();
        foreach (var id in ids)
        {
            orders.Add((await documents.DocumentAsync(id, ct).ConfigureAwait(false))!);
        }

        var first = orders[0];
        var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == bill.BillTypeId).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var day = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = bill.BillTypeId, CategoryIds = categories, DocumentDate = day, PostingDate = day, FromId = first.FromId, FromName = first.FromName, ToId = first.ToId, ToName = first.ToName,
            PaymentMethodId = first.PaymentMethodId, TableId = first.TableId, CreatedFromIds = ids,
            Transactions = [.. orders.SelectMany(o => o.Transactions).Select(l => l with { IdSup = l.Id, Id = null, Code = null, DocumentId = null, Posted = false })],
        }, ct).ConfigureAwait(false);
        var posted = await documents.PostAsync(saved.Id!.Value, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return posted;
    }

    /// <summary>Cancels an order not billed yet. What was produced for it stays in stock.</summary>
    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        if (await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                   where l.Active && d.Active && l.Document1Id == id select d.Id).AnyAsync(ct).ConfigureAwait(false))
        {
            throw ValidationException.For("id", $"{ErrorCodes.NotAllowedError}: this order is billed; reverse the bill instead.");
        }

        await documents.DeleteAsync(id, ct).ConfigureAwait(false);
    }
}

using System.Globalization;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>A contract's billing plan.</summary>
public sealed record ContractBillingDto(Guid ContractId, int EveryMonths, DateOnly NextDate);

/// <summary>A contract waiting for its invoice.</summary>
public sealed record DueContractDto(Guid ContractId, string? Code, string? Partner, int EveryMonths, DateOnly NextDate, DateOnly? EndDate, decimal Total);

/// <summary>What a run did: the invoices it made and the contracts it refused, each with why.</summary>
public sealed record InvoiceRunDto(IReadOnlyList<DocumentRefDto> Made, IReadOnlyList<RefusedContractDto> Refused);

public sealed record DocumentRefDto(Guid Id, string? Code, string? Contract);

public sealed record RefusedContractDto(Guid ContractId, string? Code, string Reason);

/// <summary>A contract ending soon, not renewed yet.</summary>
public sealed record RenewalDto(Guid ContractId, string? Code, string? Partner, DateOnly EndDate, int DaysLeft, decimal Total);

/// <summary>
/// Contract billing and renewals (the founder, 2026-11-02). A plan bills a contract every so many months: a person presses the run, which makes a draft invoice through the contract's own flow, for the
/// whole contract, no days counted, and moves the next date on in the same transaction, so nothing is billed twice. Renewing is always by hand: a new contract linked to the old.
/// </summary>
public sealed class ContractBillingService(ErpDbContext db, DocumentService documents, Visibility visibility, TimeProvider clock)
{
    public async Task<ContractBillingDto?> PlanAsync(Guid contractId, CancellationToken ct)
    {
        var row = await db.Set<ContractBilling>().AsNoTracking().FirstOrDefaultAsync(b => b.Active && b.ContractId == contractId, ct).ConfigureAwait(false);
        return row is null ? null : new ContractBillingDto(row.ContractId, row.EveryMonths, row.NextDate);
    }

    /// <summary>Sets the plan; none given stops the billing.</summary>
    public async Task SetPlanAsync(Guid contractId, int? everyMonths, DateOnly? nextDate, CancellationToken ct)
    {
        var contract = await ContractAsync(contractId, ct).ConfigureAwait(false);
        var row = await db.Set<ContractBilling>().FirstOrDefaultAsync(b => b.Active && b.ContractId == contractId, ct).ConfigureAwait(false);
        if (everyMonths is null)
        {
            if (row is not null)
            {
                row.Active = false;
            }
        }
        else
        {
            if (everyMonths is < 1 or > 60 || nextDate is null)
            {
                throw ValidationException.For("everyMonths", "A plan bills every 1 to 60 months, from a next date.");
            }

            if (row is null)
            {
                db.Add(new ContractBilling { ContractId = contractId, EveryMonths = everyMonths.Value, NextDate = nextDate.Value, TenantId = contract.TenantId, OwnerId = contract.OwnerId });
            }
            else
            {
                (row.EveryMonths, row.NextDate) = (everyMonths.Value, nextDate.Value);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The contracts due up to a day.</summary>
    public async Task<IReadOnlyList<DueContractDto>> DueAsync(DateOnly until, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var rows = await (from b in db.Set<ContractBilling>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on b.ContractId equals d.Id
                          where b.Active && b.NextDate <= until && d.Active && companies.Contains(d.CompanyId) orderby b.NextDate select new { Billing = b, Contract = d }).ToListAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(r => r.Contract.Id).ToList();
        var totals = await TotalsAsync(ids, ct).ConfigureAwait(false);
        var ends = await EndDatesAsync(rows.Select(r => r.Contract).ToList(), ct).ConfigureAwait(false);
        // A contract that has ended is not billed again: its next day is after its end date.
        rows = [.. rows.Where(r => !ends.TryGetValue(r.Contract.Id, out var last) || r.Billing.NextDate <= last)];
        return [.. rows.Select(r => new DueContractDto(r.Contract.Id, r.Contract.Code, r.Contract.ToName ?? r.Contract.FromName, r.Billing.EveryMonths, r.Billing.NextDate,
            ends.TryGetValue(r.Contract.Id, out var end) ? end : null, totals.GetValueOrDefault(r.Contract.Id)))];
    }

    /// <summary>Bills the contracts due up to a day, or those named: one refused does not stop the others.</summary>
    public async Task<InvoiceRunDto> RunAsync(DateOnly until, IReadOnlyCollection<Guid>? contracts, CancellationToken ct)
    {
        var made = new List<DocumentRefDto>();
        var refused = new List<RefusedContractDto>();
        foreach (var due in await DueAsync(until, ct).ConfigureAwait(false))
        {
            if (contracts is { Count: > 0 } && !contracts.Contains(due.ContractId))
            {
                continue;
            }

            try
            {
                var invoice = await BillAsync(due.ContractId, ct).ConfigureAwait(false);
                made.Add(new DocumentRefDto(invoice.Id!.Value, invoice.Code, due.Code));
            }
            catch (ValidationException e)
            {
                db.ChangeTracker.Clear();
                refused.Add(new RefusedContractDto(due.ContractId, due.Code, string.Join(' ', e.Errors.Values)));
            }
        }

        return new InvoiceRunDto(made, refused);
    }

    /// <summary>One draft invoice from the contract, its next date moved on: both or neither.</summary>
    public async Task<DocumentDto> BillAsync(Guid contractId, CancellationToken ct)
    {
        var contract = await ContractAsync(contractId, ct).ConfigureAwait(false);
        // The flow to an invoice (a type that holds an open item) comes first, then by the target types' order; never the contract's own type.
        var target = await (from f in db.Set<DocumentTypeFlow>().AsNoTracking() join t in db.Set<DocumentType>().AsNoTracking() on f.TargetTypeId equals t.Id
                            where f.SourceTypeId == contract.TypeId && f.TargetTypeId != contract.TypeId
                            orderby t.HoldsOpenItem descending, t.SortOrder, t.Code select (Guid?)f.TargetTypeId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: this contract type makes no invoice.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Making the invoice moves the plan's next date (DocumentService.MakeAsync), as it does when a person makes one from the contract.
        var invoice = await documents.MakeAsync(contractId, target, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return invoice;
    }

    /// <summary>The contracts that have ended or end within so many days, and are not renewed yet.</summary>
    public async Task<IReadOnlyList<RenewalDto>> RenewalsAsync(int days, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var types = db.Set<DocumentType>().Where(t => t.BillsByPlan).Select(t => t.Id);
        var contracts = await db.Set<Document>().AsNoTracking().Where(d => d.Active && types.Contains(d.TypeId) && companies.Contains(d.CompanyId)).ToListAsync(ct).ConfigureAwait(false);
        var ids = contracts.Select(c => (Guid?)c.Id).ToList();
        var copies = await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join old in db.Set<Document>().AsNoTracking() on l.Document1Id equals old.Id
                            join made in db.Set<Document>().AsNoTracking() on l.Document2Id equals made.Id
                            where l.Active && made.Active && ids.Contains(l.Document1Id) && old.TypeId == made.TypeId select new { Old = old.Id, Made = made }).ToListAsync(ct).ConfigureAwait(false);
        var ends = await EndDatesAsync(contracts, ct).ConfigureAwait(false);
        // A renewal sets a later end date; a plain copy (Generate, Repeat) does not, so it does not count as renewed.
        var copyEnds = await EndDatesAsync([.. copies.Select(c => c.Made)], ct).ConfigureAwait(false);
        var renewed = copies.Where(c => ends.TryGetValue(c.Old, out var oldEnd) && copyEnds.TryGetValue(c.Made.Id, out var newEnd) && newEnd > oldEnd).Select(c => c.Old).ToHashSet();
        var totals = await TotalsAsync(contracts.Select(c => c.Id).ToList(), ct).ConfigureAwait(false);
        return [.. contracts.Where(c => !renewed.Contains(c.Id) && ends.TryGetValue(c.Id, out var end) && end <= today.AddDays(days))
            .Select(c => new RenewalDto(c.Id, c.Code, c.ToName ?? c.FromName, ends[c.Id], ends[c.Id].DayNumber - today.DayNumber, totals.GetValueOrDefault(c.Id))).OrderBy(r => r.EndDate)];
    }

    /// <summary>
    /// A new contract from the old, by hand: its lines and properties copied, the new end date set, linked to the old, and its billing carried over from the day after the old one
    /// ends. It stays a draft to change the price on. The old one stops billing.
    /// </summary>
    public async Task<DocumentDto> RenewAsync(Guid contractId, DateOnly endDate, CancellationToken ct)
    {
        var contract = await ContractAsync(contractId, ct).ConfigureAwait(false);
        var flow = await db.Set<DocumentTypeFlow>().AsNoTracking().FirstOrDefaultAsync(f => f.SourceTypeId == contract.TypeId && f.TargetTypeId == contract.TypeId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: this contract type can't be renewed.");
        var endInfo = await EndInfoAsync(contract.TypeId, ct).ConfigureAwait(false);
        var ends = await EndDatesAsync([contract], ct).ConfigureAwait(false);
        if (ends.TryGetValue(contractId, out var oldEnd) && endDate <= oldEnd)
        {
            throw ValidationException.For("endDate", "The new end date is after the contract's end.");
        }

        var source = await documents.DocumentAsync(contractId, ct).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var made = await documents.MakeAsync(contractId, flow.TargetTypeId, ct).ConfigureAwait(false);
        var values = new Dictionary<Guid, JsonNode?>(source!.Values.Select(v => KeyValuePair.Create(v.Key, v.Value?.DeepClone())));
        if (endInfo is { } info)
        {
            values[info] = JsonValue.Create(endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var saved = await documents.SaveAsync(made with { Values = values }, ct).ConfigureAwait(false);
        if (await db.Set<ContractBilling>().FirstOrDefaultAsync(b => b.Active && b.ContractId == contractId, ct).ConfigureAwait(false) is { } plan)
        {
            plan.Active = false;
            db.Add(new ContractBilling { ContractId = saved.Id!.Value, EveryMonths = plan.EveryMonths, NextDate = oldEnd == default ? plan.NextDate : oldEnd.AddDays(1), TenantId = contract.TenantId, OwnerId = contract.OwnerId });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return saved;
    }

    private async Task<Document> ContractAsync(Guid id, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var types = db.Set<DocumentType>().Where(t => t.BillsByPlan).Select(t => t.Id);
        return await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.Active && types.Contains(d.TypeId) && companies.Contains(d.CompanyId), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("contractId", "There is no such contract.");
    }

    // The property holding a contract's end date: the one its invoice flow stops at.
    private async Task<Guid?> EndInfoAsync(Guid typeId, CancellationToken ct) =>
        await db.Set<DocumentTypeFlow>().AsNoTracking().Where(f => f.SourceTypeId == typeId && f.ValidUntilInfoId != null).Select(f => f.ValidUntilInfoId).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    private async Task<Dictionary<Guid, DateOnly>> EndDatesAsync(IReadOnlyList<Document> contracts, CancellationToken ct)
    {
        var ends = new Dictionary<Guid, DateOnly>();
        foreach (var type in contracts.GroupBy(c => c.TypeId))
        {
            if (await EndInfoAsync(type.Key, ct).ConfigureAwait(false) is not { } info)
            {
                continue;
            }

            var ids = type.Select(c => c.Id).ToList();
            var rows = await db.Set<DocumentInfo>().AsNoTracking().Where(v => v.Active && v.TypeInfoId == info && ids.Contains(v.ElementId)).ToListAsync(ct).ConfigureAwait(false);
            foreach (var row in rows)
            {
                if (row.Value?.ToString() is { Length: >= 10 } text && DateOnly.TryParse(text[..10], CultureInfo.InvariantCulture, out var end))
                {
                    ends[row.ElementId] = end;
                }
            }
        }

        return ends;
    }

    private async Task<Dictionary<Guid, decimal>> TotalsAsync(List<Guid> ids, CancellationToken ct) =>
        await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.Active && t.DocumentId != null && ids.Contains(t.DocumentId.Value)).GroupBy(t => t.DocumentId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(t => t.ValueVatLcu ?? 0) }).ToDictionaryAsync(x => x.Id, x => x.Total, ct).ConfigureAwait(false);
}

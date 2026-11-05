using System.Globalization;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>A posted invoice not yet covered: Receivable when the client owes it, otherwise it is owed to the supplier.</summary>
public sealed record OpenItemDto(Guid Id, string? Code, DateOnly? Date, DateOnly? DueDate, Guid PartnerId, string? Partner, bool Receivable, decimal Total, decimal Open, int DaysOverdue);

/// <summary>What one partner has open, by how late it is.</summary>
public sealed record AgeingRowDto(Guid PartnerId, string? Partner, bool Receivable, decimal NotDue, decimal Days30, decimal Days60, decimal Days90, decimal Over90, decimal Total);

/// <summary>
/// Which invoices are open, and payments closing them (the founder, 2026-11-02): a payment line names the internal code of the document it pays, by hand, and must cover all that
/// is open on it. Nothing is matched automatically, nothing closes in part. A payment line naming no document stays credit from the payer.
/// </summary>
public sealed class OpenItems(ErpDbContext db, Visibility visibility)
{
    /// <summary>The invoices open on a day, of one partner or all.</summary>
    public async Task<IReadOnlyList<OpenItemDto>> ListAsync(DateOnly asOf, Guid? partnerId, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var holding = db.Set<DocumentType>().Where(t => t.HoldsOpenItem).Select(t => t.Id);
        var documents = await db.Set<Document>().AsNoTracking().Where(d => d.Active && d.Posted && holding.Contains(d.TypeId) && companies.Contains(d.CompanyId) && d.PostingDate <= asOf
            && (partnerId == null || d.FromId == partnerId || d.ToId == partnerId)).ToListAsync(ct).ConfigureAwait(false);
        var ids = documents.Select(d => d.Id).ToList();
        var sides = documents.SelectMany(d => new[] { d.FromId, d.ToId }).OfType<Guid>().Distinct().ToList();
        var partners = (await db.Set<Partner>().AsNoTracking().Where(p => sides.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var totals = await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.Active && t.DocumentId != null && ids.Contains(t.DocumentId.Value)).GroupBy(t => t.DocumentId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(t => t.ValueVatLcu ?? 0) }).ToDictionaryAsync(x => x.Id, x => x.Total, ct).ConfigureAwait(false);
        var payments = await (from l in db.Set<InterDocumentDocument>().AsNoTracking()
                              join b in db.Set<Document>().AsNoTracking() on l.Document1Id equals b.Id
                              join p in db.Set<Document>().AsNoTracking() on l.Document2Id equals p.Id
                              join t in db.Set<Transaction>().AsNoTracking() on p.Id equals t.DocumentId
                              where l.Active && t.Active && p.Active && p.Posted && p.PostingDate <= asOf && ids.Contains(b.Id) && t.ReferenceNumber == b.Code
                              select new { Bill = b.Id, Payment = p.Id, p.PostingDate, Amount = t.ValueVatLcu ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var paymentIds = payments.Select(p => (Guid?)p.Payment).Distinct().ToList();
        var undone = (await db.Set<Document>().AsNoTracking().Where(r => r.Active && r.Posted && r.PostingDate <= asOf && paymentIds.Contains(r.ReversedDocumentId))
            .Select(r => r.ReversedDocumentId!.Value).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var reversed = await (from r in db.Set<Document>().AsNoTracking() join t in db.Set<DocumentTotal>().AsNoTracking() on r.Id equals t.DocumentId
                              where r.Active && t.Active && r.Posted && r.PostingDate <= asOf && r.ReversedDocumentId != null && ids.Contains(r.ReversedDocumentId.Value)
                              select new { Bill = r.ReversedDocumentId!.Value, r.PostingDate, Total = t.ValueVatLcu ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        // In the currency of the day asked: what was posted before a change of the company's currency, at its rate.
        var periods = await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, companies, asOf, ct).ConfigureAwait(false);
        decimal At(decimal amount, DateOnly? day) => Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(amount, Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(periods, day));
        return [.. documents.Select(d =>
        {
            var total = At(totals.GetValueOrDefault(d.Id), d.PostingDate);
            var open = total - payments.Where(p => p.Bill == d.Id && !undone.Contains(p.Payment)).Sum(p => At(p.Amount, p.PostingDate))
                - reversed.Where(r => r.Bill == d.Id).Sum(r => At(r.Total, r.PostingDate));
            var receivable = d.ToId is { } to && partners.Contains(to);
            var partner = receivable ? d.ToId : d.FromId is { } from && partners.Contains(from) ? d.FromId : null;
            var due = d.DueDate ?? d.DocumentDate ?? d.PostingDate;
            return (Item: new OpenItemDto(d.Id, d.Code, d.DocumentDate ?? d.PostingDate, due, partner ?? Guid.Empty, receivable ? d.ToName : d.FromName, receivable, total, open,
                due is { } day ? Math.Max(0, asOf.DayNumber - day.DayNumber) : 0), Partner: partner);
        }).Where(x => x.Partner is not null && Math.Round(x.Item.Open, 2) > 0).Select(x => x.Item).OrderBy(x => x.Partner).ThenBy(x => x.DueDate)];
    }

    /// <summary>The open invoices on a day per partner: not due yet, 1–30, 31–60, 61–90 and over 90 days late.</summary>
    public async Task<IReadOnlyList<AgeingRowDto>> AgeingAsync(DateOnly asOf, CancellationToken ct)
    {
        static decimal Sum(IEnumerable<OpenItemDto> items, Func<OpenItemDto, bool> within) => items.Where(within).Sum(i => i.Open);
        return [.. (await ListAsync(asOf, null, ct).ConfigureAwait(false)).GroupBy(i => (i.PartnerId, i.Partner, i.Receivable)).Select(g => new AgeingRowDto(g.Key.PartnerId, g.Key.Partner, g.Key.Receivable,
            Sum(g, i => i.DaysOverdue == 0), Sum(g, i => i.DaysOverdue is >= 1 and <= 30), Sum(g, i => i.DaysOverdue is >= 31 and <= 60), Sum(g, i => i.DaysOverdue is >= 61 and <= 90),
            Sum(g, i => i.DaysOverdue > 90), g.Sum(i => i.Open))).OrderBy(r => r.Partner)];
    }

    /// <summary>The day an invoice falls due when nobody set one: its date plus the partner's payment days.</summary>
    public async Task<DateOnly?> DueDateAsync(Document document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        var days = await db.Set<Partner>().AsNoTracking().Where(p => p.Id == document.FromId || p.Id == document.ToId).Select(p => p.PaymentDays).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return (document.DocumentDate ?? document.PostingDate)?.AddDays(days ?? 0);
    }

    /// <summary>
    /// Checks a payment before it posts: every document its lines name must be open at that partner and be covered in full. Links the payment to each, so the reports and a
    /// reversal find it.
    /// </summary>
    public async Task CoverAsync(Document payment, IReadOnlyList<Transaction> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(lines);
        var named = lines.Where(l => !string.IsNullOrWhiteSpace(l.ReferenceNumber)).GroupBy(l => l.ReferenceNumber!.Trim(), StringComparer.Ordinal).ToList();
        if (named.Count == 0 || payment.PostingDate is not { } day)
        {
            return;
        }

        var partner = await db.Set<Partner>().AsNoTracking().Where(p => p.Id == payment.FromId || p.Id == payment.ToId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        // Money from the partner covers what they owe us; money to them covers what we owe them (the side the partner stands on says which).
        var receivable = partner is not null && partner == payment.FromId;
        var open = partner is null ? [] : (await ListAsync(day, partner, ct).ConfigureAwait(false)).Where(i => i.Receivable == receivable).ToList();
        var errors = new Dictionary<string, string>();
        foreach (var group in named)
        {
            var paid = Math.Round(group.Sum(l => l.ValueVatLcu ?? 0), 2);
            if (open.FirstOrDefault(i => i.Code == group.Key) is not { } item)
            {
                errors[$"referenceNumber.{group.Key}"] = $"{ErrorCodes.OpenItemError}: there is no open document {group.Key} of this client or supplier.";
            }
            else if (Math.Round(item.Open, 2) != paid)
            {
                errors[$"referenceNumber.{group.Key}"] = $"{ErrorCodes.OpenItemError}: a payment covers the whole document: {group.Key} has {item.Open.ToString("0.00", CultureInfo.InvariantCulture)} open, the payment gives {paid.ToString("0.00", CultureInfo.InvariantCulture)}.";
            }
            else if (!await db.Set<InterDocumentDocument>().AnyAsync(l => l.Document1Id == item.Id && l.Document2Id == payment.Id, ct).ConfigureAwait(false))
            {
                db.Add(new InterDocumentDocument { Document1Id = item.Id, Document2Id = payment.Id, TenantId = payment.TenantId, OwnerId = payment.OwnerId });
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}

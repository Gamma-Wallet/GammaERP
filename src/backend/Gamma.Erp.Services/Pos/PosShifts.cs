using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Pos;

/// <summary>A shift. Its expected cash stays empty until the count is in.</summary>
public sealed record PosShiftDto(Guid Id, Guid PointOfSaleId, string? PointOfSale, string? CashDesk, string? OpenedBy, DateTimeOffset OpenedOn, decimal OpeningCash, string? ClosedBy, DateTimeOffset? ClosedOn,
    decimal? ExpectedCash, decimal? CountedCash, decimal? Difference, string? Note);

/// <summary>What came in by one payment method; a null method is the cash desk's own.</summary>
public sealed record ShiftPaymentDto(Guid? MethodId, string? Method, bool CountsAsCash, decimal Amount);

/// <summary>What a shift took so far.</summary>
public sealed record PosShiftSummaryDto(PosShiftDto Shift, int Bills, decimal Sales, IReadOnlyList<ShiftPaymentDto> Payments, decimal MoneyIn, decimal MoneyOut);

public sealed record OpenShiftRequest(Guid PointOfSaleId, decimal OpeningCash);

public sealed record CloseShiftRequest(decimal CountedCash, string? Note);

/// <summary>
/// Shifts and cash-up (the founder, 2026-11-02), always by hand: one open shift per point of sale, opened with the cash in the drawer, closed with the cash counted. Expected cash is
/// the opening cash plus what the cash desk took in cash, from the posted money documents of the shift, and is shown only after the count. Which methods are cash and whether a point of
/// sale needs a shift are configuration. A closed shift is final and posts nothing.
/// </summary>
public sealed class PosShifts(ErpDbContext db, RequestContext request, Visibility visibility, TimeProvider clock)
{
    private sealed record Setting(Guid? Bill, Guid? MoneyIn, Guid? MoneyOut, Guid? CashDesk, bool RequiresShift, Guid? Order = null);

    /// <summary>The open shift of a point of sale, if any.</summary>
    public async Task<PosShiftDto?> CurrentAsync(Guid pointOfSale, string language, CancellationToken ct)
    {
        var shift = await db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.Active && s.PointOfSaleId == pointOfSale && s.ClosedOn == null, ct).ConfigureAwait(false);
        return shift is null ? null : (await ToDtosAsync([shift], language, ct).ConfigureAwait(false))[0];
    }

    /// <summary>Opens a shift with the cash in the drawer.</summary>
    public async Task<PosShiftDto> OpenAsync(OpenShiftRequest open, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(open);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("pointOfSaleId", "Shifts belong to a company: choose one of its nodes.");
        if (open.OpeningCash < 0)
        {
            throw ValidationException.For("openingCash", "The opening cash can't be below zero.");
        }

        if (await db.Set<PosShift>().AsNoTracking().AnyAsync(s => s.Active && s.PointOfSaleId == open.PointOfSaleId && s.ClosedOn == null, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("pointOfSaleId", "This point of sale already has an open shift.");
        }

        var setting = await SettingAsync(open.PointOfSaleId, ct).ConfigureAwait(false);
        // Expected cash is read from the cash desk: two shifts open on one desk would count each other's money.
        if (setting.CashDesk is { } desk && await db.Set<PosShift>().AsNoTracking().AnyAsync(s => s.Active && s.CashDeskId == desk && s.ClosedOn == null, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("cashDeskId", "Another point of sale has a shift open on this cash desk: close it first, or give each point of sale its own cash desk.");
        }

        var shift = new PosShift
        {
            CompanyId = company, PointOfSaleId = open.PointOfSaleId, CashDeskId = setting.CashDesk ?? throw ValidationException.For("cashDeskId", "This point of sale has no cash desk."),
            OpenedBy = request.UserId, OpenedOn = clock.GetUtcNow(), OpeningCash = open.OpeningCash, TenantId = request.TenantId, OwnerId = company,
        };
        db.Add(shift);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ToDtosAsync([shift], language, ct).ConfigureAwait(false))[0];
    }

    /// <summary>A shift with what it took so far.</summary>
    public async Task<PosShiftSummaryDto> SummaryAsync(Guid id, string language, CancellationToken ct) =>
        await SummaryAsync(await ShiftAsync(id, ct).ConfigureAwait(false), language, ct).ConfigureAwait(false);

    /// <summary>Closes a shift with the cash counted; only now is the expected cash worked out and shown.</summary>
    public async Task<PosShiftSummaryDto> CloseAsync(Guid id, CloseShiftRequest close, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(close);
        if (close.CountedCash < 0)
        {
            throw ValidationException.For("countedCash", "The counted cash can't be below zero.");
        }

        var shift = await ShiftAsync(id, ct).ConfigureAwait(false);
        if (shift.ClosedOn is not null)
        {
            throw ValidationException.For("id", "This shift is closed.");
        }

        (shift.ClosedOn, shift.ClosedBy, shift.CountedCash, shift.Note) = (clock.GetUtcNow(), request.UserId, close.CountedCash, string.IsNullOrWhiteSpace(close.Note) ? null : close.Note.Trim());
        var taken = await TakenAsync(shift, language, ct).ConfigureAwait(false);
        shift.ExpectedCash = Math.Round(shift.OpeningCash + taken.Payments.Where(p => p.CountsAsCash).Sum(p => p.Amount) + taken.MoneyIn - taken.MoneyOut, 2);
        shift.Difference = close.CountedCash - shift.ExpectedCash;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await SummaryAsync(shift, language, ct).ConfigureAwait(false);
    }

    /// <summary>The shifts, newest first.</summary>
    public async Task<IReadOnlyList<PosShiftDto>> ListAsync(string language, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var shifts = await db.Set<PosShift>().AsNoTracking().Where(s => s.Active && companies.Contains(s.CompanyId)).OrderByDescending(s => s.OpenedOn).Take(500).ToListAsync(ct).ConfigureAwait(false);
        return await ToDtosAsync(shifts, language, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Why a point of sale's document can't go on: it needs an open shift. A bill or an order is checked at the point of sale its lines come from; money in or out of the till at
    /// the cash desk its lines name, when a point of sale that needs a shift uses that desk. Null when it can. The server checks it, not only the screen.
    /// </summary>
    public async Task<string?> RefusalAsync(Document document, IReadOnlyList<Transaction> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lines);
        var rows = await db.Set<PointOfSaleSetting>().AsNoTracking().Where(s => s.Active && (s.BillTypeId == document.TypeId || s.OrderTypeId == document.TypeId
            || s.MoneyInTypeId == document.TypeId || s.MoneyOutTypeId == document.TypeId || s.CashDeskId != null)).ToListAsync(ct).ConfigureAwait(false);
        if (!rows.Any(s => s.BillTypeId == document.TypeId || s.OrderTypeId == document.TypeId || s.MoneyInTypeId == document.TypeId || s.MoneyOutTypeId == document.TypeId))
        {
            return null;
        }

        foreach (var from in lines.Select(l => l.FromId ?? document.FromId).OfType<Guid>().Distinct())
        {
            var place = document.OwnerId is { } owner && owner != from && await PosPlaces.PointOfSaleOfAsync(db, owner, ct).ConfigureAwait(false) == from ? owner : from;
            var setting = await SettingAsync(place, ct).ConfigureAwait(false);
            if ((setting.Bill == document.TypeId || setting.Order == document.TypeId) && setting.RequiresShift && !await OpenAtAsync(place, ct).ConfigureAwait(false))
            {
                return "Open a shift first: this point of sale has none open.";
            }
        }

        foreach (var desk in lines.Select(l => l.ElementId).OfType<Guid>().Distinct())
        {
            if (await db.Set<PosShift>().AsNoTracking().AnyAsync(s => s.Active && s.CashDeskId == desk && s.ClosedOn == null, ct).ConfigureAwait(false))
            {
                continue;
            }

            foreach (var place in await PlacesUsingAsync(rows, desk, ct).ConfigureAwait(false))
            {
                var setting = await SettingAsync(place, ct).ConfigureAwait(false);
                if (setting.CashDesk == desk && setting.RequiresShift && (setting.MoneyIn == document.TypeId || setting.MoneyOut == document.TypeId))
                {
                    return "Open a shift first: the point of sale of this cash desk has none open.";
                }
            }
        }

        return null;
    }

    private Task<bool> OpenAtAsync(Guid place, CancellationToken ct) =>
        db.Set<PosShift>().AsNoTracking().AnyAsync(s => s.Active && s.PointOfSaleId == place && s.ClosedOn == null, ct);

    // The points of sale whose settings may name a cash desk: those with a row of their own naming it, those of a kind whose row names it, and, when the default row names it,
    // every point of sale of a kind that has settings.
    private async Task<List<Guid>> PlacesUsingAsync(List<PointOfSaleSetting> rows, Guid desk, CancellationToken ct)
    {
        var naming = rows.Where(r => r.CashDeskId == desk).ToList();
        var places = naming.Select(r => r.PointOfSaleId).OfType<Guid>().ToHashSet();
        var kinds = naming.Where(r => r.PointOfSaleId == null).Select(r => r.PointOfSaleTypeId).OfType<Guid>().ToList();
        if (naming.Any(r => r.PointOfSaleId == null && r.PointOfSaleTypeId == null))
        {
            kinds.AddRange(await db.Set<PointOfSaleSetting>().AsNoTracking().Where(r => r.Active && r.PointOfSaleTypeId != null).Select(r => r.PointOfSaleTypeId!.Value).ToListAsync(ct).ConfigureAwait(false));
        }

        if (kinds.Count > 0)
        {
            places.UnionWith(await db.Set<CostCenter>().AsNoTracking().Where(c => c.Active && kinds.Contains(c.TypeId)).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false));
        }

        return [.. places];
    }

    private async Task<PosShift> ShiftAsync(Guid id, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        return await db.Set<PosShift>().FirstOrDefaultAsync(s => s.Id == id && s.Active && companies.Contains(s.CompanyId), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The shift does not exist.");
    }

    private async Task<PosShiftSummaryDto> SummaryAsync(PosShift shift, string language, CancellationToken ct)
    {
        var taken = await TakenAsync(shift, language, ct).ConfigureAwait(false);
        return taken with { Shift = (await ToDtosAsync([shift], language, ct).ConfigureAwait(false))[0] };
    }

    // What the shift's posted documents took: its bills, and the money documents with a line on its cash desk. A reversal counts the other way.
    private async Task<PosShiftSummaryDto> TakenAsync(PosShift shift, string language, CancellationToken ct)
    {
        var setting = await SettingAsync(shift.PointOfSaleId, ct).ConfigureAwait(false);
        var (opened, until) = (shift.OpenedOn, shift.ClosedOn ?? DateTimeOffset.MaxValue);
        var wanted = new[] { setting.Bill, setting.MoneyIn, setting.MoneyOut }.OfType<Guid>().ToList();
        var types = await db.Set<DocumentType>().AsNoTracking().Where(t => wanted.Contains(t.Id) || (t.ReversedDocumentTypeId != null && wanted.Contains(t.ReversedDocumentTypeId.Value)))
            .Select(t => new { t.Id, Base = t.ReversedDocumentTypeId ?? t.Id, Reversal = t.ReversedDocumentTypeId != null }).ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var ids = types.Keys.ToList();
        var documents = db.Set<Document>().AsNoTracking().Where(d => d.Active && d.Posted && ids.Contains(d.TypeId) && d.CreatedOn >= opened && d.CreatedOn <= until);

        var money = await (from t in db.Set<Transaction>().AsNoTracking() join d in documents on t.DocumentId equals d.Id
                           where t.Active && t.ElementId == shift.CashDeskId
                           select new { d.Id, d.TypeId, d.PaymentMethodId, Amount = t.ValueVatLcu ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var moneyIds = money.Select(m => (Guid?)m.Id).Distinct().ToList();
        var paying = (await db.Set<InterDocumentDocument>().AsNoTracking().Where(l => l.Active && moneyIds.Contains(l.Document2Id)).Select(l => l.Document2Id!.Value).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var methodIds = money.Select(m => m.PaymentMethodId).OfType<Guid>().Distinct().ToList();
        var methods = await db.Set<PaymentMethod>().AsNoTracking().Where(p => methodIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        decimal Signed(Guid type, decimal amount) => (types[type].Base == setting.MoneyIn) != types[type].Reversal ? amount : -amount;

        var payments = money.Where(m => paying.Contains(m.Id)).GroupBy(m => m.PaymentMethodId).Select(g =>
        {
            var method = g.Key is { } k ? methods.GetValueOrDefault(k) : null;
            return new ShiftPaymentDto(g.Key, method?.Name?.In(language), method?.IsCash ?? true, g.Sum(m => Signed(m.TypeId, m.Amount)));
        }).OrderBy(p => p.Method).ToList();
        var loose = money.Where(m => !paying.Contains(m.Id)).Select(m => Signed(m.TypeId, m.Amount)).ToList();

        var billIds = await (from t in db.Set<Transaction>().AsNoTracking() join d in documents on t.DocumentId equals d.Id
                             where t.Active && (t.FromId == shift.PointOfSaleId || t.ToId == shift.PointOfSaleId || d.TableId == shift.PointOfSaleId)
                             select new { d.Id, d.TypeId }).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var bills = billIds.Where(b => types[b.TypeId].Base == setting.Bill).ToList();
        var nullableBills = bills.Select(b => (Guid?)b.Id).ToList();
        var totals = await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.Active && nullableBills.Contains(t.DocumentId)).GroupBy(t => t.DocumentId!.Value)
            .Select(g => new { g.Key, Total = g.Sum(t => t.ValueVatLcu ?? 0) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct).ConfigureAwait(false);
        var sales = bills.Sum(b => (types[b.TypeId].Reversal ? -1 : 1) * totals.GetValueOrDefault(b.Id));
        return new PosShiftSummaryDto(null!, bills.Count(b => !types[b.TypeId].Reversal), sales, payments, loose.Where(a => a > 0).Sum(), -loose.Where(a => a < 0).Sum());
    }

    // The point of sale's own settings over its kind's over the default's, as the point of sale reads them. An empty "requires a shift" means it does.
    private async Task<Setting> SettingAsync(Guid pointOfSale, CancellationToken ct)
    {
        // A till's own row first, then its shop's, the shop's kind's and the default's.
        var (_, chain) = await PosPlaces.ChainAsync(db, await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false), pointOfSale, ct).ConfigureAwait(false);
        Guid? Pick(Func<PointOfSaleSetting, Guid?> column) => chain.Select(column).FirstOrDefault(v => v is not null);
        return new Setting(Pick(r => r.BillTypeId), Pick(r => r.MoneyInTypeId), Pick(r => r.MoneyOutTypeId), Pick(r => r.CashDeskId), chain.Select(r => r.RequiresShift).FirstOrDefault(v => v is not null) ?? true,
            Pick(r => r.OrderTypeId));
    }

    private async Task<List<PosShiftDto>> ToDtosAsync(List<PosShift> shifts, string language, CancellationToken ct)
    {
        var places = shifts.SelectMany(s => new[] { s.PointOfSaleId, s.CashDeskId }).Distinct().ToList();
        var names = await db.Set<CostCenter>().AsNoTracking().Where(c => places.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(language) ?? c.Code, ct).ConfigureAwait(false);
        var people = shifts.SelectMany(s => new[] { s.OpenedBy, s.ClosedBy }).OfType<Guid>().Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name.In(language) ?? u.Username, ct).ConfigureAwait(false);
        string? Who(Guid? user) => user is { } u ? users.GetValueOrDefault(u) : null;
        return [.. shifts.Select(s => new PosShiftDto(s.Id, s.PointOfSaleId, names.GetValueOrDefault(s.PointOfSaleId), names.GetValueOrDefault(s.CashDeskId), Who(s.OpenedBy), s.OpenedOn, s.OpeningCash, Who(s.ClosedBy), s.ClosedOn,
            s.ExpectedCash, s.CountedCash, s.Difference, s.Note))];
    }
}

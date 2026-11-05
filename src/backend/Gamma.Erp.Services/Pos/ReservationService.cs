using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gamma.Erp.Services.Pos;

/// <summary>A show with how full it is.</summary>
public sealed record PosShowDto(Guid Id, string? Name, DateOnly Day, TimeOnly? StartsAt, Guid AuditoriumId, string? Auditorium, Guid ProductId, decimal? Price, int Seats, int Taken);

/// <summary>A seat held at a show: by whom, on which bill.</summary>
public sealed record TakenSeatDto(Guid SeatId, Guid? BillId, string? Guest);

/// <summary>Seats to book at a show.</summary>
public sealed record BookSeatsRequest(Guid ShowId, IReadOnlyList<Guid> SeatIds, string? Guest = null);

/// <summary>A room held from a day to the day it is left.</summary>
public sealed record PosStayDto(Guid Id, Guid RoomId, string? Guest, DateOnly FromDate, DateOnly ToDate, decimal? Price, Guid? BillId, DateOnly? CheckedInOn = null);

/// <summary>A room to book.</summary>
public sealed record BookStayRequest(Guid RoomId, DateOnly FromDate, DateOnly ToDate, string? Guest = null, decimal? Price = null);

/// <summary>
/// Reservations at a point of sale (the founder, 2026-10-31). Events: a show's seats are booked and billed at once, the bill then paid like any
/// other. Stays: a room is held by nights and stays open; check-out makes the bill for the nights slept. No place is sold twice: the database
/// refuses it (<see cref="PosReservation"/>). Which product a stay sells, which bill and client are the point of sale's settings.
/// </summary>
public sealed class ReservationService(ErpDbContext db, Visibility visibility, DocumentService documents, PosService pos, TimeProvider clock)
{
    // ---------- Events ----------

    /// <summary>The shows of the auditoriums under a point of sale, from a day on (by default today), the soonest first.</summary>
    public async Task<IReadOnlyList<PosShowDto>> ShowsAsync(Guid pointOfSale, DateOnly? from, string language, CancellationToken ct)
    {
        var day = from ?? Today;
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        var below = Below(tree, pointOfSale);
        var shows = (await db.Set<PosShow>().AsNoTracking().Where(s => s.Day >= day).ToListAsync(ct).ConfigureAwait(false))
            .Where(s => below.Contains(s.AuditoriumId)).OrderBy(s => s.Day).ThenBy(s => s.StartsAt).ToList();
        var ids = shows.Select(s => (Guid?)s.Id).ToList();
        var taken = (await db.Set<PosReservation>().AsNoTracking().Where(r => ids.Contains(r.ShowId)).Select(r => r.ShowId!.Value).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        return [.. shows.Select(s => new PosShowDto(s.Id, s.Name.In(language), s.Day, s.StartsAt, s.AuditoriumId, tree.GetValueOrDefault(s.AuditoriumId)?.Name.In(language), s.ProductId,
            s.Price, Below(tree, s.AuditoriumId).Count(p => !tree.Values.Any(c => c.IdSup == p)), taken.GetValueOrDefault(s.Id)))];
    }

    /// <summary>The seats held at a show.</summary>
    public async Task<IReadOnlyList<TakenSeatDto>> TakenAsync(Guid show, CancellationToken ct) =>
        await db.Set<PosReservation>().AsNoTracking().Where(r => r.ShowId == show).Select(r => new TakenSeatDto(r.PlaceId, r.BillId, r.GuestName)).ToListAsync(ct).ConfigureAwait(false);

    /// <summary>Books seats at a show: all of them or none, on one posted bill, returned to be paid at once.</summary>
    public async Task<Guid> BookSeatsAsync(Guid pointOfSale, BookSeatsRequest book, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(book);
        var seats = book.SeatIds.Distinct().ToList();
        if (seats.Count == 0)
        {
            throw ValidationException.For("seatIds", "Choose a seat first.");
        }

        var show = await db.Set<PosShow>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == book.ShowId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("showId", "The show does not exist.");
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        var inHall = Below(tree, show.AuditoriumId);
        if (!Below(tree, pointOfSale).Contains(show.AuditoriumId) || seats.Any(s => !inHall.Contains(s) || tree.Values.Any(c => c.IdSup == s)))
        {
            throw ValidationException.For("seatIds", "A seat is not one of this show's.");
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var rows = seats.Select(s => new PosReservation { PointOfSaleId = pointOfSale, PlaceId = s, ShowId = show.Id, GuestName = Clean(book.Guest), OwnerId = pointOfSale }).ToList();
        db.AddRange(rows);
        await HoldAsync("seatIds", "A seat has just been taken: choose another.", ct).ConfigureAwait(false);
        var bill = await BillAsync(pointOfSale, show.ProductId, seats.Count, show.Price, book.Guest, null, language, ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            row.BillId = bill;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return bill;
    }

    /// <summary>Cancels the seats of one bill: the bill is reversed and they are free again. Money already taken is paid back out of the cash desk.</summary>
    public async Task CancelSeatsAsync(Guid bill, CancellationToken ct)
    {
        var rows = await db.Set<PosReservation>().Where(r => r.BillId == bill && r.ShowId != null).ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            throw ValidationException.For("billId", "No seats are held on this bill.");
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        await documents.ReverseAsync(bill, null, Today, ct).ConfigureAwait(false);
        // Money taken for it goes back out of the cash desk: each payment's line for this bill is reversed (the payment's reversal type, e.g. the point-of-sale refund).
        var code = await db.Set<Document>().AsNoTracking().Where(d => d.Id == bill).Select(d => d.Code).FirstAsync(ct).ConfigureAwait(false);
        var payments = await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join p in db.Set<Document>().AsNoTracking() on l.Document2Id equals p.Id
                              where l.Active && l.Document1Id == bill && p.Active && p.Posted && p.ReversedDocumentId == null select p.Id).Distinct().ToListAsync(ct).ConfigureAwait(false);
        foreach (var payment in payments)
        {
            var lines = await db.Set<Transaction>().AsNoTracking().Where(t => t.Active && t.DocumentId == payment && t.ReferenceNumber == code).Select(t => new { t.Id, t.Quantity }).ToListAsync(ct).ConfigureAwait(false);
            if (lines.Count > 0)
            {
                await documents.ReverseAsync(payment, lines.ToDictionary(l => l.Id, l => l.Quantity ?? 1), Today, ct).ConfigureAwait(false);
            }
        }

        foreach (var row in rows)
        {
            row.Active = false;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    // ---------- Stays ----------

    /// <summary>The stays under a point of sale that touch these days, checked out or not.</summary>
    public async Task<IReadOnlyList<PosStayDto>> StaysAsync(Guid pointOfSale, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var below = Below(await TreeAsync(ct).ConfigureAwait(false), pointOfSale);
        var stays = (await db.Set<PosReservation>().AsNoTracking().Where(r => r.ShowId == null && r.FromDate < to && r.ToDate > from).ToListAsync(ct).ConfigureAwait(false))
            .Where(r => below.Contains(r.PlaceId)).ToList();
        // A stay whose bill was reversed is open again: it shows with no bill, to be checked out again.
        var bills = stays.Select(r => r.BillId).OfType<Guid>().Select(b => (Guid?)b).ToList();
        var reversed = (await db.Set<Document>().AsNoTracking().Where(d => d.Active && d.Posted && bills.Contains(d.ReversedDocumentId)).Select(d => d.ReversedDocumentId!.Value).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        return [.. stays.Select(r => ToDto(r) with { BillId = r.BillId is { } b && reversed.Contains(b) ? null : r.BillId })];
    }

    /// <summary>Holds a room from a day to the day it is left; refused when it is taken on any of those nights.</summary>
    public async Task<PosStayDto> BookStayAsync(Guid pointOfSale, BookStayRequest book, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.ToDate <= book.FromDate)
        {
            throw ValidationException.For("toDate", "A stay ends on a later day than it starts.");
        }

        var tree = await TreeAsync(ct).ConfigureAwait(false);
        if (!Below(tree, pointOfSale).Contains(book.RoomId) || tree.Values.Any(c => c.IdSup == book.RoomId))
        {
            throw ValidationException.For("roomId", "The room is not one of this point of sale's.");
        }

        var row = new PosReservation { PointOfSaleId = pointOfSale, PlaceId = book.RoomId, GuestName = Clean(book.Guest), FromDate = book.FromDate, ToDate = book.ToDate,
            Price = book.Price, OwnerId = pointOfSale };
        db.Add(row);
        await HoldAsync("roomId", "The room is taken on those days.", ct).ConfigureAwait(false);
        return ToDto(row);
    }

    /// <summary>Checks a guest in by hand on the day they arrive: from the stay's first day, while it is not checked out.</summary>
    public async Task<PosStayDto> CheckInAsync(Guid stay, CancellationToken ct)
    {
        var row = await db.Set<PosReservation>().FirstOrDefaultAsync(r => r.Id == stay && r.ShowId == null && r.Active, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The stay does not exist.");
        if (await BilledAsync(row, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("id", "The stay is already checked out.");
        }

        if (Today < row.FromDate)
        {
            throw ValidationException.For("id", $"The stay starts on {row.FromDate:yyyy-MM-dd}: check in from that day.");
        }

        row.CheckedInOn ??= Today;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row);
    }

    /// <summary>Extends a stay not yet checked out to a later end day; the room must be free on the added days.</summary>
    public async Task<PosStayDto> ExtendStayAsync(Guid stay, DateOnly toDate, CancellationToken ct)
    {
        var row = await db.Set<PosReservation>().FirstOrDefaultAsync(r => r.Id == stay && r.ShowId == null && r.Active, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The stay does not exist.");
        if (await BilledAsync(row, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("id", "The stay is already checked out.");
        }

        if (toDate <= row.ToDate)
        {
            throw ValidationException.For("toDate", "Extending moves the stay's end to a later day.");
        }

        row.ToDate = toDate;
        await HoldAsync("toDate", "The room is taken on those days.", ct).ConfigureAwait(false);
        return ToDto(row);
    }

    /// <summary>
    /// Checks a stay out: it ends today unless it ended sooner, at least one night, and its nights go on one posted bill at the room, returned to be paid. A stay that has not
    /// started is cancelled instead. A stay whose bill was reversed is checked out again.
    /// </summary>
    public async Task<Guid> CheckOutAsync(Guid stay, string language, CancellationToken ct)
    {
        var row = await db.Set<PosReservation>().FirstOrDefaultAsync(r => r.Id == stay && r.ShowId == null, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The stay does not exist.");
        if (await BilledAsync(row, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("id", "The stay is already checked out.");
        }

        if (Today < row.FromDate)
        {
            throw ValidationException.For("id", "The stay has not started: cancel it instead.");
        }

        var settings = await pos.SettingsAsync(row.PointOfSaleId, language, ct).ConfigureAwait(false);
        var product = settings.ProductId ?? throw ValidationException.For("productId", $"{ErrorCodes.NotConfiguredError}: set what a night is sold as in the point of sale's settings.");
        var (start, planned) = (row.FromDate!.Value, row.ToDate!.Value);
        var end = Today <= start ? start.AddDays(1) : Today < planned ? Today : planned;
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        row.ToDate = end;
        var bill = await BillAsync(row.PointOfSaleId, product, end.DayNumber - start.DayNumber, row.Price, row.GuestName, row.PlaceId, language, ct).ConfigureAwait(false);
        row.BillId = bill;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return bill;
    }

    /// <summary>Cancels a stay not yet checked out: the room is free again.</summary>
    public async Task CancelStayAsync(Guid stay, CancellationToken ct)
    {
        var row = await db.Set<PosReservation>().FirstOrDefaultAsync(r => r.Id == stay && r.ShowId == null, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The stay does not exist.");
        if (await BilledAsync(row, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("id", "The stay is checked out: reverse its bill.");
        }

        row.Active = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ---------- Shared ----------

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static PosStayDto ToDto(PosReservation r) => new(r.Id, r.PlaceId, r.GuestName, r.FromDate!.Value, r.ToDate!.Value, r.Price, r.BillId, r.CheckedInOn);

    /// <summary>Whether a stay is checked out: it has a bill that was not reversed.</summary>
    private async Task<bool> BilledAsync(PosReservation row, CancellationToken ct) =>
        row.BillId is { } bill && !await db.Set<Document>().AsNoTracking().AnyAsync(d => d.Active && d.Posted && d.ReversedDocumentId == bill, ct).ConfigureAwait(false);

    /// <summary>Saves what holds a place; the database's refusal of a place held twice comes back as a message.</summary>
    private async Task HoldAsync(string field, string taken, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ExclusionViolation })
        {
            throw ValidationException.For(field, taken);
        }
    }

    /// <summary>The cost centres the request sees, by id.</summary>
    private async Task<Dictionary<Guid, CostCenter>> TreeAsync(CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        return await db.Set<CostCenter>().AsNoTracking().Where(c => nodes.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Every row under this one.</summary>
    private static HashSet<Guid> Below(Dictionary<Guid, CostCenter> tree, Guid root)
    {
        var below = new HashSet<Guid>();
        for (var level = new List<Guid> { root }; level.Count > 0;)
        {
            level = [.. tree.Values.Where(c => c.IdSup is { } up && level.Contains(up) && below.Add(c.Id)).Select(c => c.Id)];
        }

        return below;
    }

    /// <summary>One posted bill of one product at a point of sale, as the settings say, to the walk-in client under the guest's name.</summary>
    private async Task<Guid> BillAsync(Guid pointOfSale, Guid product, decimal quantity, decimal? price, string? guest, Guid? place, string language, CancellationToken ct)
    {
        var settings = await pos.SettingsAsync(pointOfSale, language, ct).ConfigureAwait(false);
        if (settings.BillTypeId is not { } billType || settings.WalkInClientId is not { } client)
        {
            throw ValidationException.For("billTypeId", $"{ErrorCodes.NotConfiguredError}: the point of sale has no bill type or walk-in client.");
        }

        var proposal = await documents.LineProposalAsync(billType, product, client, null, null, language, ct).ConfigureAwait(false);
        var scope = proposal.ScopeIds.Cast<Guid?>().FirstOrDefault()
            ?? throw ValidationException.For("productId", $"{ErrorCodes.NotConfiguredError}: the product fits none of the bill's lines.");
        var lineType = await db.Set<TransactionTypeConf>().AsNoTracking().Where(c => c.Id == scope).Select(c => c.TransactionTypeId).FirstAsync(ct).ConfigureAwait(false);
        // As a new line in the document editor: the proposal's VAT group, else the bill type's first.
        var vat = proposal.VatGroupId ?? (await documents.FormAsync(billType, language, ct).ConfigureAwait(false)).VatGroups.Select(v => (Guid?)v.Id).FirstOrDefault();
        var item = await db.Set<Product>().AsNoTracking().FirstAsync(p => p.Id == product, ct).ConfigureAwait(false);
        var from = await db.Set<CostCenter>().AsNoTracking().FirstAsync(c => c.Id == pointOfSale, ct).ConfigureAwait(false);
        var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == billType).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = billType, CategoryIds = categories, DocumentDate = Today, PostingDate = Today, ToId = client, ToName = Clean(guest) ?? settings.WalkInClient,
            PaymentMethodId = settings.PaymentMethodId, TableId = place,
            Transactions = [new TransactionDto { TypeId = lineType!.Value, TypeConfId = scope, ElementId = product, ElementName = item.Name.In(language), ElementTypeId = item.TypeId,
                UnitId = proposal.UnitId, VatGroupId = vat, Quantity = quantity, Price = price ?? proposal.Price, FromId = pointOfSale, FromName = from.Name.In(language) }],
        }, ct).ConfigureAwait(false);
        return (await documents.PostAsync(saved.Id!.Value, ct).ConfigureAwait(false)).Id!.Value;
    }
}

using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Pos;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>Reservations (the founder, 2026-10-31): a show's seats paid at booking, rooms by nights open until check-out, and no place sold twice.</summary>
public sealed class ReservationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await using var context = Context(new TenantContext());
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ErpDbContext Context(TenantContext tenant) => new((DbContextOptions<ErpDbContext>)PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(),
        _db.GetConnectionString()).AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System)).Options);

    private sealed record Stage(ErpDbContext Db, Guid Tenant, Guid Company, ReservationService Reservations, PosService Pos, DocumentService Documents, Guid Service)
    {
        public Func<Guid, ReservationService> At { get; init; } = null!;
    }

    private async Task<Stage> StageAsync(ErpDbContext context, TenantContext tenant, CancellationToken ct)
    {
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Argëtimi", "AL");
        OrganisationIds company;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            await db.SaveChangesAsync(ct);
        }

        tenant.Set(tenantId, null);
        var owner = company.Corporate;
        var serviceCategory = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "SERVICE", ct);
        var serviceType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == serviceCategory.Id).Select(l => l.TypeId).FirstAsync(ct);
        var service = new Product { TypeId = serviceType, Code = "TICKET", Name = TranslatedText.English("Biletë"), TenantId = tenantId, OwnerId = owner };
        var piece = await context.Set<Unit>().SingleAsync(u => u.Code == "PIECE", ct);
        context.AddRange(service, new InterProductCategory { ElementId = service.Id, CategoryId = serviceCategory.Id, TenantId = tenantId, OwnerId = owner },
            new InterProductSpecificUnit { ProductId = service.Id, UnitId = piece.Id, SortOrder = 1, TenantId = tenantId, OwnerId = owner },
            new ProductSalePrice { ProductId = service.Id, Value = 100, CompanyId = owner, TenantId = tenantId, OwnerId = owner });
        var client = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "UNKNOWN", ct)).Id, Code = "WALK", Name = TranslatedText.English("Klient"),
            TenantId = tenantId, OwnerId = owner };
        context.Add(client);
        await context.SaveChangesAsync(ct);
        var settings = await context.Set<PointOfSaleSetting>().SingleAsync(s => s.PointOfSaleId == null && s.PointOfSaleTypeId == null, ct);
        (settings.WalkInClientId, settings.ProductId) = (client.Id, service.Id);
        await context.SaveChangesAsync(ct);

        ReservationService At(Guid node)
        {
            var request = new RequestContext(tenantId, null, node, new HashSet<Guid>(), IsAdministrator: true);
            var visibility = new Visibility(context, request);
            var documents = new DocumentService(context, request, visibility, TimeProvider.System);
            return new ReservationService(context, visibility, documents, new PosService(context, visibility, documents, TimeProvider.System), TimeProvider.System);
        }

        var request = new RequestContext(tenantId, null, owner, new HashSet<Guid>(), IsAdministrator: true);
        var visibility = new Visibility(context, request);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        return new Stage(context, tenantId, owner, At(owner), new PosService(context, visibility, documents, TimeProvider.System), documents, service.Id) { At = At };
    }

    private static async Task<CostCenter> PlaceAsync(Stage s, Guid parent, string type, string code, CancellationToken ct)
    {
        var typeId = await s.Db.Set<CostCenterType>().Where(t => t.Code == type).Select(t => t.Id).SingleAsync(ct);
        var place = new CostCenter { TypeId = typeId, IdSup = parent, Code = code, Name = TranslatedText.English(code), TenantId = s.Tenant, OwnerId = s.Company };
        s.Db.Add(place);
        await s.Db.SaveChangesAsync(ct);
        return place;
    }

    [Fact]
    public async Task A_shows_seats_are_booked_on_one_bill_never_twice_and_a_cancelled_seat_is_free_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = new TenantContext();
        await using var context = Context(new TenantContext());
        await using var db = Context(tenant);
        var s = await StageAsync(db, tenant, ct);
        var cinema = await PlaceAsync(s, s.Company, "POS_EVENTS", "CINEMA", ct);
        var hall = await PlaceAsync(s, cinema.Id, "AUDITORIUM", "SALLA", ct);
        var row = await PlaceAsync(s, hall.Id, "SEAT_ROW", "A", ct);
        var (a1, a2, a3) = (await PlaceAsync(s, row.Id, "SEAT", "A1", ct), await PlaceAsync(s, row.Id, "SEAT", "A2", ct), await PlaceAsync(s, row.Id, "SEAT", "A3", ct));
        var day = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var show = new PosShow { Name = TranslatedText.English("Hamleti"), AuditoriumId = hall.Id, ProductId = s.Service, Day = day, StartsAt = new TimeOnly(20, 0), Price = 500,
            TenantId = s.Tenant, OwnerId = s.Company };
        db.Add(show);
        await db.SaveChangesAsync(ct);

        var at = s.At(cinema.Id);
        (await at.ShowsAsync(cinema.Id, null, "en", ct)).Should().ContainSingle().Which.Should().Match<PosShowDto>(x => x.Seats == 3 && x.Taken == 0 && x.Name == "Hamleti");
        var bill = await at.BookSeatsAsync(cinema.Id, new BookSeatsRequest(show.Id, [a1.Id, a2.Id], "Ana"), "en", ct);
        var document = await db.Set<Document>().SingleAsync(d => d.Id == bill, ct);
        document.Posted.Should().BeTrue();
        document.ToName.Should().Be("Ana");
        var line = await db.Set<Gamma.Erp.Models.Transactions.Transaction>().SingleAsync(t => t.DocumentId == bill, ct);
        (line.Quantity, line.Price).Should().Be((2m, 500m));
        (await at.TakenAsync(show.Id, ct)).Select(t => t.SeatId).Should().BeEquivalentTo([a1.Id, a2.Id]);

        // A taken seat refuses the whole booking, a seat of nowhere too, and nothing is left behind.
        var bills = await db.Set<Document>().CountAsync(ct);
        var again = async () => await at.BookSeatsAsync(cinema.Id, new BookSeatsRequest(show.Id, [a3.Id, a2.Id]), "en", ct);
        (await again.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("seatIds");
        db.ChangeTracker.Clear();
        var elsewhere = async () => await at.BookSeatsAsync(cinema.Id, new BookSeatsRequest(show.Id, [row.Id]), "en", ct);
        await elsewhere.Should().ThrowAsync<ValidationException>();
        (await db.Set<Document>().CountAsync(ct)).Should().Be(bills);
        (await at.TakenAsync(show.Id, ct)).Should().HaveCount(2);

        await at.CancelSeatsAsync(bill, ct);
        (await at.TakenAsync(show.Id, ct)).Should().BeEmpty();
        (await at.BookSeatsAsync(cinema.Id, new BookSeatsRequest(show.Id, [a2.Id]), "en", ct)).Should().NotBe(bill);
    }

    [Fact]
    public async Task A_room_is_never_held_twice_stays_open_and_check_out_bills_the_nights()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = new TenantContext();
        await using var db = Context(tenant);
        var s = await StageAsync(db, tenant, ct);
        var hotel = await PlaceAsync(s, s.Company, "POS_STAYS", "HOTEL", ct);
        var floor = await PlaceAsync(s, hotel.Id, "FLOOR", "F1", ct);
        var room = await PlaceAsync(s, floor.Id, "ROOM", "101", ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var at = s.At(hotel.Id);

        var stay = await at.BookStayAsync(hotel.Id, new BookStayRequest(room.Id, today.AddDays(-3), today.AddDays(2), "Ana", 80), ct);
        var overlap = async () => await at.BookStayAsync(hotel.Id, new BookStayRequest(room.Id, today, today.AddDays(1)), ct);
        (await overlap.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("roomId");
        db.ChangeTracker.Clear();
        var backwards = async () => await at.BookStayAsync(hotel.Id, new BookStayRequest(room.Id, today, today), ct);
        await backwards.Should().ThrowAsync<ValidationException>();
        // The day it is left it is free for the night: one stay ends the day the next begins.
        var next = await at.BookStayAsync(hotel.Id, new BookStayRequest(room.Id, today.AddDays(2), today.AddDays(4), "Ben"), ct);
        (await at.StaysAsync(hotel.Id, today, today.AddDays(7), ct)).Should().HaveCount(2);
        await at.CancelStayAsync(next.Id, ct);

        // Leaving early: the nights slept, on a bill at the room.
        var bill = await at.CheckOutAsync(stay.Id, "en", ct);
        var document = await db.Set<Document>().SingleAsync(d => d.Id == bill, ct);
        (document.Posted, document.TableId, document.ToName).Should().Be((true, room.Id, "Ana"));
        var line = await db.Set<Gamma.Erp.Models.Transactions.Transaction>().SingleAsync(t => t.DocumentId == bill, ct);
        (line.Quantity, line.Price).Should().Be((3m, 80m));
        (await at.StaysAsync(hotel.Id, today.AddDays(-7), today.AddDays(7), ct)).Should().ContainSingle().Which.ToDate.Should().Be(today);
        var twice = async () => await at.CheckOutAsync(stay.Id, "en", ct);
        await twice.Should().ThrowAsync<ValidationException>();
        await at.BookStayAsync(hotel.Id, new BookStayRequest(room.Id, today, today.AddDays(1)), ct);
    }
}

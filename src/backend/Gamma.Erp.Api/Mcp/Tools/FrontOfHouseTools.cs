using System.ComponentModel;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Pos;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>
/// The front of the house as the point-of-sale screens run it: a hotel's stays, a cinema's or theatre's shows and seats, a restaurant's orders
/// sent by the waiters and billed at payment, the production points' display, and what a point of sale is set up with. Each through the
/// screen's own service, with the screen in the user's menu. Stays and seats work on their real dates: a check-out bills today.
/// </summary>
[McpServerToolType]
public sealed class FrontOfHouseTools(ErpDbContext db, RequestContext request, Visibility visibility, ReservationService reservations, PosOrders orders, PosService pos,
    ProductionDisplay display, DocumentService documents)
{
    private const string PointHelp = "The point of sale (hotel, cinema, restaurant, shop) or one of its tills: id or exact code.";

    // ---------- Hotel stays ----------

    [McpServerTool(Name = "stays", Title = "A hotel's rooms and stays", ReadOnly = true)]
    [Description("A hotel's stays between two days, as its rooms grid shows them: each with room, guest, first day, the day it is left, price per night, checked in and checked out. Needs the POS screen.")]
    public Task<object> StaysAsync([Description(PointHelp)] string pointOfSale,
        [Description("From (yyyy-mm-dd); today when left out.")] DateOnly? from = null,
        [Description("To (yyyy-mm-dd); 14 days after from when left out.")] DateOnly? to = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            var point = await PlaceAsync(pointOfSale, ct).ConfigureAwait(false);
            var first = from ?? DateOnly.FromDateTime(DateTime.Today);
            return await reservations.StaysAsync(point, first, to ?? first.AddDays(14), ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "stay", Title = "Book, check in, extend, check out or cancel a stay", ReadOnly = false, Destructive = true)]
    [Description("A hotel stay, as the rooms grid does it: action book (a room from a first day to the day it is left, optional guest and price per night without VAT; holds the room, bills nothing), check-in (from its first day on, records today), extend (a later day it is left), check-out (ends the stay today, or at its planned end, and posts one bill for the nights slept at the room, to the walk-in client under the guest's name: final; the bill is then paid with pay_pos_bills; needs an open shift when the hotel asks for one), or cancel (a stay not yet checked out). Needs the POS screen. Show the user the change and get an explicit yes; then confirm=true.")]
    public Task<object> StayAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("book, check-in, extend, check-out or cancel.")] string action,
        [Description("book: " + PointHelp)] string? pointOfSale = null,
        [Description("book: the room (id or exact code).")] string? room = null,
        [Description("book: the first night (yyyy-mm-dd).")] DateOnly? fromDate = null,
        [Description("book, extend: the day the room is left (yyyy-mm-dd).")] DateOnly? toDate = null,
        [Description("book: optional, the guest's name.")] string? guest = null,
        [Description("book: optional, the price per night without VAT; the night product's sale price when left out.")] decimal? price = null,
        [Description("check-in, extend, check-out, cancel: the stay's id (stays lists them).")] Guid? stay = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the stay and what happens to it (a check-out posts its bill: final)");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            Guid Stay() => stay ?? throw new McpException("Give the stay's id (stays lists them).");
            switch (action.Trim().ToUpperInvariant())
            {
                case "BOOK":
                    var point = await PlaceAsync(pointOfSale ?? throw new McpException("Give the hotel."), ct).ConfigureAwait(false);
                    var roomId = await PlaceAsync(room ?? throw new McpException("Give the room."), ct).ConfigureAwait(false);
                    return await reservations.BookStayAsync(point, new BookStayRequest(roomId, fromDate ?? throw new McpException("Give the first night."),
                        toDate ?? throw new McpException("Give the day the room is left."), guest, price), ct).ConfigureAwait(false);
                case "CHECK-IN" or "CHECKIN":
                    return await reservations.CheckInAsync(Stay(), ct).ConfigureAwait(false);
                case "EXTEND":
                    return await reservations.ExtendStayAsync(Stay(), toDate ?? throw new McpException("Give the new day the room is left."), ct).ConfigureAwait(false);
                case "CHECK-OUT" or "CHECKOUT":
                    var bill = await reservations.CheckOutAsync(Stay(), Registers.English, ct).ConfigureAwait(false);
                    var made = await documents.DocumentAsync(bill, ct).ConfigureAwait(false);
                    return new { checkedOut = true, bill, made?.Code, made?.Posted, note = "Pay it with pay_pos_bills; get_document shows its lines." };
                case "CANCEL":
                    await reservations.CancelStayAsync(Stay(), ct).ConfigureAwait(false);
                    return new { cancelled = true, stay };
                default:
                    throw new McpException("The action is book, check-in, extend, check-out or cancel.");
            }
        });

    // ---------- Cinema / theatre: shows and seats ----------

    [McpServerTool(Name = "shows", Title = "Shows and taken seats", ReadOnly = true)]
    [Description("A cinema's or theatre's shows from a day on (name, auditorium, day, time, product, price, free seats), or with a show's id the seats taken at it (seat, guest, bill). Shows are set up as configuration rows (model pos-shows). Needs the POS screen.")]
    public Task<object> ShowsAsync([Description(PointHelp)] string pointOfSale,
        [Description("Optional: from this day (yyyy-mm-dd); today when left out.")] DateOnly? from = null,
        [Description("Optional: a show's id, for its taken seats.")] Guid? show = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            if (show is { } id)
            {
                return await reservations.TakenAsync(id, ct).ConfigureAwait(false);
            }

            return await reservations.ShowsAsync(await PlaceAsync(pointOfSale, ct).ConfigureAwait(false), from, Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "book_seats", Title = "Book or cancel seats at a show", ReadOnly = false, Destructive = true)]
    [Description("Seats at a show, as the seat plan does it: action book (all the seats or none, one posted bill with the seats as the quantity of the show's product, to the walk-in client, optional guest name; final; then paid with pay_pos_bills; needs an open shift when the point of sale asks for one), or cancel (by the bill: the bill is reversed whole, its payments' lines too, and all its seats are free again). Needs the POS screen. Show the user the seats and the amount and get an explicit yes; then confirm=true.")]
    public Task<object> BookSeatsAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("book or cancel.")] string action,
        [Description("book: " + PointHelp)] string? pointOfSale = null,
        [Description("book: the show's id (shows lists them).")] Guid? show = null,
        [Description("book: the seats (ids or exact codes).")] IReadOnlyList<string>? seats = null,
        [Description("book: optional, the guest's name.")] string? guest = null,
        [Description("cancel: the bill's id.")] Guid? bill = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the seats, the show and the amount (posted at once: final)");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            switch (action.Trim().ToUpperInvariant())
            {
                case "BOOK":
                    var point = await PlaceAsync(pointOfSale ?? throw new McpException("Give the point of sale."), ct).ConfigureAwait(false);
                    var seatIds = new List<Guid>();
                    foreach (var seat in seats ?? throw new McpException("Choose a seat first."))
                    {
                        seatIds.Add(await PlaceAsync(seat, ct).ConfigureAwait(false));
                    }

                    var made = await reservations.BookSeatsAsync(point, new BookSeatsRequest(show ?? throw new McpException("Give the show."), seatIds, guest), Registers.English, ct)
                        .ConfigureAwait(false);
                    var document = await documents.DocumentAsync(made, ct).ConfigureAwait(false);
                    return new { booked = seatIds.Count, bill = made, document?.Code, document?.Posted, note = "Pay it with pay_pos_bills." };
                case "CANCEL":
                    var id = bill ?? throw new McpException("Give the bill.");
                    await reservations.CancelSeatsAsync(id, ct).ConfigureAwait(false);
                    return new { cancelled = true, bill = id };
                default:
                    throw new McpException("The action is book or cancel.");
            }
        });

    // ---------- Restaurant: orders sent by waiters ----------

    [McpServerTool(Name = "pos_orders", Title = "Open orders of a restaurant", ReadOnly = true)]
    [Description("The orders sent by the waiters and not yet billed, of a point of sale's order type (from its settings), optionally at one table: each with table, client, total. Orders themselves are written with save_draft_document (the order type) and post_document. Needs the POS screen.")]
    public Task<object> PosOrdersAsync([Description(PointHelp)] string pointOfSale,
        [Description("Optional: one table (id or exact code).")] string? table = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            var point = await PlaceAsync(pointOfSale, ct).ConfigureAwait(false);
            var settings = await pos.SettingsAsync(point, Registers.English, ct).ConfigureAwait(false);
            var orderType = settings.OrderTypeId ?? throw new McpException("This point of sale sends no orders: its bill is written at once.");
            var tableId = table is null ? (Guid?)null : await PlaceAsync(table, ct).ConfigureAwait(false);
            return await orders.OpenAsync(orderType, tableId, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "bill_pos_orders", Title = "Bill or cancel waiters' orders", ReadOnly = false, Destructive = true)]
    [Description("Waiters' orders, as the till does it: action bill (the named orders, or all of a table's, become one posted bill of the point of sale's bill type: final; then paid with pay_pos_bills), or cancel (an order not billed is switched off). Needs the POS screen. Show the user the orders and the total and get an explicit yes; then confirm=true.")]
    public Task<object> BillPosOrdersAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("bill or cancel.")] string action,
        [Description(PointHelp)] string pointOfSale,
        [Description("bill: optional, the table (id or exact code) whose orders are billed.")] string? table = null,
        [Description("bill: optional, the orders' ids; cancel: the one order's id.")] IReadOnlyList<Guid>? orderIds = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the orders to bill or cancel (a bill is posted at once: final)");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            var point = await PlaceAsync(pointOfSale, ct).ConfigureAwait(false);
            switch (action.Trim().ToUpperInvariant())
            {
                case "BILL":
                    var settings = await pos.SettingsAsync(point, Registers.English, ct).ConfigureAwait(false);
                    var tableId = table is null ? (Guid?)null : await PlaceAsync(table, ct).ConfigureAwait(false);
                    var made = await orders.BillAsync(new BillOrdersRequest(settings.BillTypeId ?? throw new McpException("The point of sale has no bill type."),
                        settings.OrderTypeId ?? throw new McpException("This point of sale sends no orders."), tableId, orderIds), ct).ConfigureAwait(false);
                    return new { made.Id, made.Code, made.Posted, note = "Pay it with pay_pos_bills." };
                case "CANCEL":
                    var order = orderIds is { Count: 1 } one ? one[0] : throw new McpException("Give the one order to cancel.");
                    await orders.CancelAsync(order, ct).ConfigureAwait(false);
                    return new { cancelled = true, order };
                default:
                    throw new McpException("The action is bill or cancel.");
            }
        });

    // ---------- Production points (kitchen, bar) ----------

    [McpServerTool(Name = "production_display", Title = "What a kitchen or bar has to make", ReadOnly = true)]
    [Description("The production points' display: with nothing given, the company's production points (kitchen, bar…) and the products no route sends anywhere; with a point, its board: the order lines to make, oldest first, and what it made last. Needs the Production display screen.")]
    public Task<object> ProductionDisplayAsync([Description("Optional: the production point (id or exact code).")] string? point = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "production-display", ct).ConfigureAwait(false);
            if (point is null)
            {
                return new
                {
                    points = await display.PointsAsync(Registers.English, ct).ConfigureAwait(false),
                    notRouted = await display.NotRoutedAsync(ct).ConfigureAwait(false),
                };
            }

            return await display.BoardAsync(await PlaceAsync(point, ct).ConfigureAwait(false), Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "mark_produced", Title = "Mark order lines made", ReadOnly = false, Destructive = true)]
    [Description("Says order lines were made at a production point, as its display does: for a product with a recipe this makes and posts the production the order's type names (final, not reversed). With undo=true, takes back lines marked by mistake (only those that made nothing). Needs the Production display screen. Show the user the lines and get an explicit yes; then confirm=true.")]
    public Task<object> MarkProducedAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The production point (id or exact code).")] string point,
        [Description("The order lines' ids (production_display lists them).")] IReadOnlyList<Guid> lines,
        [Description("True: undo the marks.")] bool undo = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the lines made (a production is posted at once: final)");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "production-display", ct).ConfigureAwait(false);
            var pointId = await PlaceAsync(point, ct).ConfigureAwait(false);
            if (undo)
            {
                await display.UndoAsync(pointId, lines, ct).ConfigureAwait(false);
            }
            else
            {
                await display.ProducedAsync(pointId, lines, ct).ConfigureAwait(false);
            }

            return await display.BoardAsync(pointId, Registers.English, ct).ConfigureAwait(false);
        });

    // ---------- What a point of sale works with ----------

    [McpServerTool(Name = "pos_info", Title = "A point of sale's settings, places, menus and barcodes", ReadOnly = true)]
    [Description("What a point of sale works with: what=settings (bill, order, money in/out types, cash desk, walk-in client, payment method, night product, whether it needs a shift, what is missing), places (its floor plan: tables, rooms, seats, with their properties and open bills), menus (the sale menus open at a time, with categories, products and prices), barcodes (the products' barcodes), or methods (the payment methods offered at a cost centre). Menus and barcodes need the POS screen.")]
    public Task<object> PosInfoAsync([Description("settings, places, menus, barcodes or methods.")] string what,
        [Description(PointHelp + " For methods, any cost centre.")] string? pointOfSale = null,
        [Description("menus: optional, the time (hh:mm); now when left out.")] TimeOnly? at = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var point = pointOfSale is null ? (Guid?)null : await PlaceAsync(pointOfSale, ct).ConfigureAwait(false);
            switch (what.Trim().ToUpperInvariant())
            {
                case "SETTINGS":
                    return await pos.SettingsAsync(point, Registers.English, ct).ConfigureAwait(false);
                case "PLACES":
                    return await pos.PlacesAsync(point ?? throw new McpException("Give the point of sale."), Registers.English, ct).ConfigureAwait(false);
                case "MENUS":
                    await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
                    return await pos.CurrentMenusAsync(point, at, Registers.English, ct).ConfigureAwait(false);
                case "BARCODES":
                    await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
                    return await pos.BarcodesAsync(Registers.English, ct).ConfigureAwait(false);
                case "METHODS":
                    await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
                    return await pos.MethodsAtAsync(point ?? throw new McpException("Give the cost centre."), ct).ConfigureAwait(false);
                default:
                    throw new McpException("What is settings, places, menus, barcodes or methods.");
            }
        });

    private async Task<Guid> PlaceAsync(string given, CancellationToken ct) =>
        await Registers.ElementAsync(s => Registers.CostCenters(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), given, "cost centre")
            .ConfigureAwait(false);
}

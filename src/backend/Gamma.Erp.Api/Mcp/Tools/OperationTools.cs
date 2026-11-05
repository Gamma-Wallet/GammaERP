using System.ComponentModel;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Pos;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PaymentMethod = Gamma.Erp.Models.Payments.PaymentMethod;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>An ingredient as Claude gives it, when a run used other quantities than the recipe says.</summary>
public sealed record IngredientLine(
    [property: Description("The ingredient: its id or exact code.")] string Product,
    [property: Description("How much goes out of stock, waste included (for a disassembly: how much comes back).")] decimal Quantity,
    [property: Description("Optional: the unit (id, code or name); the product's default when left out.")] string? Unit = null,
    [property: Description("Optional: how much of the quantity is waste (for a disassembly: how much is lost).")] decimal Waste = 0);

/// <summary>
/// The work of the Production, Stock convert and point-of-sale screens: making a product by its recipe (or taking it apart), splitting stock into
/// another unit, opening and closing a till's shift, and paying the bills a till left open. Each goes through the screen's own service, with its
/// screen in the user's menu; production, disassembly, a split and a payment are documents that are posted at once, so they are final.
/// </summary>
[McpServerToolType]
public sealed class OperationTools(ErpDbContext db, RequestContext request, Visibility visibility, ProductionService production, UnitConversionService conversion,
    PosShifts shifts, PosService pos, ProductUnitService productUnits)
{
    [McpServerTool(Name = "calculate_production", Title = "What a production run takes", ReadOnly = true)]
    [Description("Works out what making a quantity of a product takes, by its recipe: each ingredient with the quantity that goes out (waste included), the waste, and what the place holds now. With disassemble=true, what taking it apart gives back and what is lost. Needs the Production screen. Read it and show it to the user before produce.")]
    public Task<object> CalculateProductionAsync(
        [Description("The product to make: id or exact code.")] string product,
        [Description("How much to make (or take apart).")] decimal quantity,
        [Description("Optional: the unit (id, code or name); the product's default when left out.")] string? unit = null,
        [Description("Optional: the place it is made at (id or exact code), to show what it holds.")] string? place = null,
        [Description("True: taking the product apart instead.")] bool disassemble = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "production", ct).ConfigureAwait(false);
            var productId = await ProductAsync(product, ct).ConfigureAwait(false);
            var unitId = await UnitAsync(productId, unit, ct).ConfigureAwait(false);
            var placeId = place is null ? (Guid?)null : await PlaceAsync(place, ct).ConfigureAwait(false);
            if (disassemble)
            {
                var back = await production.CalculateDisassemblyAsync(productId, unitId, quantity, placeId, ct).ConfigureAwait(false);
                return new { disassemble = true, lines = back.Select(l => new { l.ProductId, l.Name, unit = l.UnitId, l.Recovered, l.Lost, held = l.Held }) };
            }

            var lines = await production.CalculateAsync(productId, unitId, quantity, placeId, ct).ConfigureAwait(false);
            return new { disassemble = false, lines = lines.Select(l => new { l.ProductId, l.Name, unit = l.UnitId, l.Quantity, waste = l.WasteQuantity, held = l.Held }) };
        });

    [McpServerTool(Name = "produce", Title = "Produce, or take a product apart", ReadOnly = false, Destructive = true)]
    [Description("Makes a quantity of a product at a place, as the Production screen does: a Production document takes the ingredients out (by the recipe, or as given in lines), puts the product in at what they cost and is posted at once, so it is final (a production is not reversed). With an order id, produces what is left of that customer order. With disassemble=true, takes the product apart instead (the recipe backwards; what is not recovered is waste). Needs the Production screen. Show the user calculate_production's lines, say it is final, and get an explicit yes; then confirm=true.")]
    public Task<object> ProduceAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The place it is made at, whose stock the ingredients come from: id or exact code.")] string place,
        [Description("The product to make: id or exact code (left out with an order).")] string? product = null,
        [Description("How much to make (left out with an order).")] decimal? quantity = null,
        [Description("Optional: the unit (id, code or name); the product's default when left out.")] string? unit = null,
        [Description("Optional: the place the product goes to (id or exact code); the same place when left out.")] string? toPlace = null,
        [Description("Optional: the day; today when left out.")] DateOnly? date = null,
        [Description("Optional: a note.")] string? note = null,
        [Description("Optional: the ingredients actually used, instead of the recipe's.")] IReadOnlyList<IngredientLine>? lines = null,
        [Description("Optional: the id of a customer order (a type listed on the Production screen) to produce what is left of.")] Guid? order = null,
        [Description("Optional: the production document type (id, code or name) when the company has several.")] string? type = null,
        [Description("True: take the product apart instead.")] bool disassemble = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "what the run takes and makes, and that it is posted at once (final)");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "production", ct).ConfigureAwait(false);
            var placeId = await PlaceAsync(place, ct).ConfigureAwait(false);
            if (order is { } orderId && product is null)
            {
                var made = await production.ProduceOrderAsync(orderId, placeId, date, ct).ConfigureAwait(false);
                return new { produced = made.Select(Shown) };
            }

            var productId = await ProductAsync(product ?? throw new McpException("Give the product (or an order)."), ct).ConfigureAwait(false);
            var unitId = await UnitAsync(productId, unit, ct).ConfigureAwait(false);
            var amount = quantity ?? throw new McpException("Give the quantity.");
            var toId = toPlace is null ? (Guid?)null : await PlaceAsync(toPlace, ct).ConfigureAwait(false);
            var typeId = type is null ? (Guid?)null : await StockTypeAsync(disassemble ? "disassembly" : "production", type, ct).ConfigureAwait(false);
            if (disassemble)
            {
                List<DisassemblyLineDto>? back = null;
                if (lines is not null)
                {
                    back = [];
                    foreach (var l in lines)
                    {
                        var id = await ProductAsync(l.Product, ct).ConfigureAwait(false);
                        back.Add(new DisassemblyLineDto(id, null, await UnitAsync(id, l.Unit, ct).ConfigureAwait(false), l.Quantity, l.Waste));
                    }
                }

                var taken = await production.DisassembleAsync(new DisassembleRequest(placeId, productId, unitId, amount, date, note, back, toId, typeId), ct).ConfigureAwait(false);
                return Shown(taken);
            }

            List<ProductionLineDto>? used = null;
            if (lines is not null)
            {
                used = [];
                foreach (var l in lines)
                {
                    var id = await ProductAsync(l.Product, ct).ConfigureAwait(false);
                    used.Add(new ProductionLineDto(id, null, await UnitAsync(id, l.Unit, ct).ConfigureAwait(false), l.Quantity, l.Waste));
                }
            }

            var document = await production.ProduceAsync(new ProduceRequest(placeId, productId, unitId, amount, date, order, note, used, toId, typeId), ct).ConfigureAwait(false);
            return Shown(document);
        });

    [McpServerTool(Name = "convert_stock", Title = "Split stock into another unit", ReadOnly = false, Destructive = true)]
    [Description("Moves a quantity of a product held at a place from one unit to another (1 kg tub into 10 cups of 100 g, a box into pieces), as the Stock convert screen does: a Unit split document, posted at once, so it is final; the value does not change. Needs the company's conversion between the two units. Needs the Stock convert screen. Show the user what will move, say it is final, and get an explicit yes; then confirm=true.")]
    public Task<object> ConvertStockAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The place that holds it: id or exact code.")] string place,
        [Description("The product: id or exact code.")] string product,
        [Description("The unit it is held in now (id, code or name).")] string fromUnit,
        [Description("The unit it goes into (id, code or name).")] string toUnit,
        [Description("How much, in the unit it is held in now.")] decimal quantity,
        [Description("Optional: the day; today when left out.")] DateOnly? date = null,
        [Description("Optional: a note.")] string? note = null,
        [Description("Optional: the unit split document type (id, code or name) when the company has several.")] string? type = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "what moves into which unit, and that it is posted at once (final)");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "stock-convert", ct).ConfigureAwait(false);
            var placeId = await PlaceAsync(place, ct).ConfigureAwait(false);
            var productId = await ProductAsync(product, ct).ConfigureAwait(false);
            var from = await UnitAsync(productId, fromUnit, ct).ConfigureAwait(false);
            var to = await UnitAsync(productId, toUnit, ct).ConfigureAwait(false);
            var typeId = type is null ? (Guid?)null : await StockTypeAsync("unit-split", type, ct).ConfigureAwait(false);
            var document = await conversion.ConvertAsync(new ConvertStockRequest(placeId, productId, from, to, quantity, date, note, typeId), ct).ConfigureAwait(false);
            return Shown(document);
        });

    [McpServerTool(Name = "pos_shift", Title = "A till's shift", ReadOnly = false, Destructive = false)]
    [Description("A point of sale's shift and cash-up, as the till does: action current (the open shift and what it took so far), open (with the cash in the drawer), close (with the cash counted; the expected cash and the difference are shown only then; a closed shift is final), or list (every shift, newest first; needs the POS shifts screen). Open and close change something: show the user what and get an explicit yes; then confirm=true. Needs the POS screen.")]
    public Task<object> PosShiftAsync(
        [Description("current, open, close or list.")] string action,
        [Description("The point of sale or till: id or exact code (not needed for list).")] string? pointOfSale = null,
        [Description("open: the cash in the drawer; close: the cash counted.")] decimal? cash = null,
        [Description("close: an optional note.")] string? note = null,
        [Description("True only after the user said yes (open, close).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var what = action.Trim().ToUpperInvariant();
            if (what == "LIST")
            {
                await Registers.Screen(visibility, "pos-shifts", ct).ConfigureAwait(false);
                return await shifts.ListAsync(Registers.English, ct).ConfigureAwait(false);
            }

            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            var point = await PlaceAsync(pointOfSale ?? throw new McpException("Give the point of sale."), ct).ConfigureAwait(false);
            var current = await shifts.CurrentAsync(point, Registers.English, ct).ConfigureAwait(false);
            switch (what)
            {
                case "CURRENT":
                    return current is null ? new { open = false } : await shifts.SummaryAsync(current.Id, Registers.English, ct).ConfigureAwait(false);
                case "OPEN":
                    Erp.NeedsYes(confirm, "the shift to open and the cash in the drawer");
                    return await shifts.OpenAsync(new OpenShiftRequest(point, cash ?? throw new McpException("Give the cash in the drawer.")), Registers.English, ct).ConfigureAwait(false);
                case "CLOSE":
                    Erp.NeedsYes(confirm, "the shift to close and the cash counted (a closed shift is final)");
                    var open = current ?? throw new McpException("This point of sale has no open shift.");
                    return await shifts.CloseAsync(open.Id, new CloseShiftRequest(cash ?? throw new McpException("Give the cash counted."), note), Registers.English, ct)
                        .ConfigureAwait(false);
                default:
                    throw new McpException("The action is current, open, close or list.");
            }
        });

    [McpServerTool(Name = "unpaid_pos_bills", Title = "A till's open bills", ReadOnly = true)]
    [Description("The bills of a point of sale (its bill type, from its settings) not yet paid: each with its table, client, total, paid and open amount; and the point of sale's settings (bill and payment types, cash desk, walk-in client, payment method, what is still missing). Needs the POS screen.")]
    public Task<object> UnpaidPosBillsAsync(
        [Description("The point of sale or till: id or exact code.")] string pointOfSale,
        [Description("Optional: only this table or place (id or exact code).")] string? table = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            var point = await PlaceAsync(pointOfSale, ct).ConfigureAwait(false);
            var settings = await pos.SettingsAsync(point, Registers.English, ct).ConfigureAwait(false);
            var tableId = table is null ? (Guid?)null : await PlaceAsync(table, ct).ConfigureAwait(false);
            var bills = settings.BillTypeId is { } billType ? await pos.UnpaidAsync(billType, null, tableId, ct, point).ConfigureAwait(false) : [];
            return new { settings, bills };
        });

    [McpServerTool(Name = "pay_pos_bills", Title = "Pay a till's open bills", ReadOnly = false, Destructive = true)]
    [Description("Pays open bills of a point of sale, as the till's Pay does: the named bills, a table's, or all of them, with one money document per client (the point of sale's payment type, into its cash desk, or where the payment method's link sends it), linked to the bills it pays and posted at once, so it is final. Needs the POS screen. Show the user the bills and the amount, say it is final, and get an explicit yes; then confirm=true.")]
    public Task<object> PayPosBillsAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The point of sale or till: id or exact code.")] string pointOfSale,
        [Description("Optional: the ids of the bills to pay (unpaid_pos_bills lists them).")] IReadOnlyList<Guid>? bills = null,
        [Description("Optional: pay every open bill of this table or place (id or exact code).")] string? table = null,
        [Description("True: pay every open bill of the point of sale.")] bool all = false,
        [Description("Optional: the payment method (id, code or name); the point of sale's when left out.")] string? method = null,
        [Description("Optional: the day; today when left out.")] DateOnly? date = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the bills to pay, the amount and the method, and that the payment is posted at once (final)");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "pos", ct).ConfigureAwait(false);
            var point = await PlaceAsync(pointOfSale, ct).ConfigureAwait(false);
            var settings = await pos.SettingsAsync(point, Registers.English, ct).ConfigureAwait(false);
            if (settings.BillTypeId is not { } billType || settings.MoneyInTypeId is not { } moneyIn || settings.CashDeskId is not { } desk)
            {
                throw new McpException($"The point of sale is not set up to take money yet; missing: {string.Join(", ", settings.Missing)} (point-of-sale settings in the configuration).");
            }

            if (!all && table is null && bills is not { Count: > 0 })
            {
                throw new McpException("Name the bills, a table, or all=true.");
            }

            var tableId = table is null ? (Guid?)null : await PlaceAsync(table, ct).ConfigureAwait(false);
            var methodId = method is null ? settings.PaymentMethodId : await MethodAsync(method, ct).ConfigureAwait(false);
            var paid = await pos.PayAsync(new PayBillsRequest(billType, moneyIn, desk, bills, tableId, all, methodId, date, settings.PointOfSaleId ?? point), ct).ConfigureAwait(false);
            return new { payments = paid.Select(Shown) };
        });

    private static object Shown(DocumentDto d) => new
    {
        d.Id, d.Code, name = Registers.Name(d.Name), d.Posted, d.PostingDate, from = d.FromName, to = d.ToName,
        lines = d.Transactions.Count, note = "get_document shows its lines and postings.",
    };

    private async Task<Guid> ProductAsync(string given, CancellationToken ct) =>
        await Registers.ElementAsync(s => Registers.Products(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), given, "product")
            .ConfigureAwait(false);

    private async Task<Guid> PlaceAsync(string given, CancellationToken ct) =>
        await Registers.ElementAsync(s => Registers.CostCenters(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), given, "cost centre")
            .ConfigureAwait(false);

    /// <summary>A unit by id, code or name; none given, the product's default (its own first unit, or its type's).</summary>
    private async Task<Guid> UnitAsync(Guid product, string? given, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(given))
        {
            var offered = await productUnits.OfferedAsync(product, ct).ConfigureAwait(false);
            return offered.Count > 0 ? offered[0] : throw new McpException("The product has no unit: give the unit.");
        }

        return await Registers.ElementAsync(s => Registers.Units(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), given, "unit")
            .ConfigureAwait(false);
    }

    /// <summary>The company's document type of a stock kind (production, disassembly, unit-split) by id, code or name.</summary>
    private async Task<Guid> StockTypeAsync(string kind, string given, CancellationToken ct)
    {
        var ids = await production.TypesOfAsync(kind, ct).ConfigureAwait(false);
        var types = await db.Set<Gamma.Erp.Models.Documents.DocumentType>().AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false);
        var key = given.Trim();
        var found = types.Where(t => Registers.Is(t.Id, key) || string.Equals(t.Code, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Name.In(Registers.English), key, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count == 1 ? found[0].Id
            : throw new McpException($"No single {kind} document type \"{key}\". Known: {string.Join(", ", types.Select(t => t.Code ?? t.Name.In(Registers.English)))}.");
    }

    private async Task<Guid> MethodAsync(string given, CancellationToken ct)
    {
        var key = given.Trim();
        var methods = await db.Set<PaymentMethod>().AsNoTracking().Where(m => m.Active).ToListAsync(ct).ConfigureAwait(false);
        var found = methods.Where(m => Registers.Is(m.Id, key) || string.Equals(m.Code, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.Name?.In(Registers.English), key, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count == 1 ? found[0].Id
            : throw new McpException($"No single payment method \"{key}\". Known: {string.Join(", ", methods.Select(m => m.Code ?? m.Name?.In(Registers.English)))}.");
    }
}

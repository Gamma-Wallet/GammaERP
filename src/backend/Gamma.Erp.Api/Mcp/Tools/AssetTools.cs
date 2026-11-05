using System.ComponentModel;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Depreciation;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>
/// Fixed assets and their depreciation, lots and serial numbers, bank statement lines and the repairs board, each as its screen does it and with
/// its screen in the user's menu. Depreciation runs, matching and repairs are started by a person: here, by the user's yes.
/// </summary>
[McpServerToolType]
public sealed class AssetTools(ErpDbContext db, RequestContext request, Visibility visibility, DocumentService documents, DepreciationService depreciation, LotService lots,
    SerialService serials, BankStatements statements, RepairService repairs, ProductUnitService productUnits)
{
    // ---------- Depreciation ----------

    [McpServerTool(Name = "depreciation", Title = "Depreciation books, assets and runs", ReadOnly = true)]
    [Description("Reads depreciation as its screen shows it: what=books (the company's books with their method and whether they post), products (the fixed-asset products), assets (a book's assets with value and book value), calculate (what a book's run on a date would depreciate, saving nothing), runs (a book's runs), run-lines (one run's lines), history (one asset value's runs). Needs the Depreciation screen.")]
    public Task<object> DepreciationAsync(
        [Description("books, products, assets, calculate, runs, run-lines or history.")] string what,
        [Description("The book: id or name (assets, calculate, runs, run-lines).")] string? book = null,
        [Description("The date (calculate, run-lines).")] DateOnly? date = null,
        [Description("history: the asset value's id (assets lists them).")] Guid? asset = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, ["depreciation", "asset-value"], [], ct).ConfigureAwait(false);
            switch (what.Trim().ToUpperInvariant())
            {
                case "BOOKS":
                    return await depreciation.BooksAsync(Registers.English, ct).ConfigureAwait(false);
                case "PRODUCTS":
                    return await depreciation.AssetProductsAsync(Registers.English, ct).ConfigureAwait(false);
            }

            await Registers.Screen(visibility, "depreciation", ct).ConfigureAwait(false);
            switch (what.Trim().ToUpperInvariant())
            {
                case "ASSETS":
                    return await depreciation.AssetsAsync(await BookAsync(book, ct).ConfigureAwait(false), Registers.English, ct).ConfigureAwait(false);
                case "CALCULATE":
                    return await depreciation.CalculateAsync(await BookAsync(book, ct).ConfigureAwait(false), date ?? throw new McpException("Give the date."), ct).ConfigureAwait(false);
                case "RUNS":
                    return await depreciation.RunsAsync(await BookAsync(book, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                case "RUN-LINES":
                    return await depreciation.RunLinesAsync(await BookAsync(book, ct).ConfigureAwait(false), date ?? throw new McpException("Give the run's date."), Registers.English, ct)
                        .ConfigureAwait(false);
                case "HISTORY":
                    return await depreciation.AssetHistoryAsync(asset ?? throw new McpException("Give the asset value's id."), ct).ConfigureAwait(false);
                default:
                    throw new McpException("What is books, products, assets, calculate, runs, run-lines or history.");
            }
        });

    [McpServerTool(Name = "run_depreciation", Title = "Register an asset or run depreciation", ReadOnly = false, Destructive = true)]
    [Description("Does depreciation work by hand, as its screen does: action register (an asset in a book: product, unit, book, initial value, start date, optional months, method, percentages, document and cost centre), run (saves a book's run on a date; show depreciation what=calculate first), journal (makes the run's journal of a document type and category; posted when the book posts, so final), undo (undoes a book's latest run; a posted journal is reversed), dispose (marks an asset value disposed, or back in use with disposed=false). Needs the Depreciation screen. Show the user what will be done and get an explicit yes; then confirm=true.")]
    public Task<object> RunDepreciationAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("register, run, journal, undo or dispose.")] string action,
        [Description("The book: id or name (register, run, journal, undo).")] string? book = null,
        [Description("The date (run, journal, undo).")] DateOnly? date = null,
        [Description("register: the product (id or exact code).")] string? product = null,
        [Description("register: the unit (id, code or name); the product's default when left out.")] string? unit = null,
        [Description("register: the initial value in LCU.")] decimal? initialValue = null,
        [Description("register: the day depreciation starts.")] DateOnly? startDate = null,
        [Description("register: optional, its useful life in months.")] int? months = null,
        [Description("register: optional, the linear percentage a year.")] decimal? linearPercentage = null,
        [Description("register: optional, the declining percentage a year.")] decimal? decliningPercentage = null,
        [Description("register: optional, the purchase document's id.")] Guid? document = null,
        [Description("register: optional, the cost centre it belongs to (id or exact code).")] string? place = null,
        [Description("journal: the document type (id, code or name).")] string? type = null,
        [Description("journal: the document category (id, code or name); the type's own when it is in one.")] string? category = null,
        [Description("dispose: the asset value's id.")] Guid? asset = null,
        [Description("dispose: true disposed, false back in use.")] bool disposed = true, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the depreciation work to do (and that a posted journal is final)");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "depreciation", ct).ConfigureAwait(false);
            switch (action.Trim().ToUpperInvariant())
            {
                case "REGISTER":
                    var productId = await Registers.ElementAsync(s => Registers.Products(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct),
                        product ?? throw new McpException("Give the product."), "product").ConfigureAwait(false);
                    var unitId = await UnitAsync(productId, unit, ct).ConfigureAwait(false);
                    var placeId = place is null ? (Guid?)null : await PlaceAsync(place, ct).ConfigureAwait(false);
                    var made = await depreciation.RegisterAsync(new AssetRegistration(productId, unitId, await BookAsync(book, ct).ConfigureAwait(false),
                        initialValue ?? throw new McpException("Give the initial value."), startDate ?? throw new McpException("Give the start date."), months, document, null,
                        linearPercentage, decliningPercentage, placeId), ct).ConfigureAwait(false);
                    return new { registered = made };
                case "RUN":
                    return await depreciation.RunAsync(await BookAsync(book, ct).ConfigureAwait(false), date ?? throw new McpException("Give the date."), ct).ConfigureAwait(false);
                case "JOURNAL":
                    var allowed = await documents.AllowedTypesAsync(null, ct).ConfigureAwait(false);
                    var typeDto = Registers.Type(allowed, type ?? throw new McpException("Give the journal's document type."), "document");
                    var categoryId = Registers.Category(await documents.Entities.CategoriesAsync(ct).ConfigureAwait(false), category, "document")
                        ?? (typeDto.CategoryIds.Count == 1 ? typeDto.CategoryIds[0] : throw new McpException("The type is in several categories: give the category."));
                    var journal = await depreciation.JournalAsync(await BookAsync(book, ct).ConfigureAwait(false), date ?? throw new McpException("Give the run's date."), typeDto.Id!.Value,
                        categoryId, ct).ConfigureAwait(false);
                    return new { journal.Id, journal.Code, journal.Posted, journal.PostingDate };
                case "UNDO":
                    var bookId = await BookAsync(book, ct).ConfigureAwait(false);
                    var day = date ?? throw new McpException("Give the latest run's date.");
                    await depreciation.UndoRunAsync(bookId, day, ct).ConfigureAwait(false);
                    return new { undone = true, book = bookId, date = day };
                case "DISPOSE":
                    var value = asset ?? throw new McpException("Give the asset value's id.");
                    await depreciation.SetDisposedAsync(value, disposed, ct).ConfigureAwait(false);
                    return new { asset = value, disposed };
                default:
                    throw new McpException("The action is register, run, journal, undo or dispose.");
            }
        });

    // ---------- Lots and serial numbers ----------

    [McpServerTool(Name = "lots_and_serials", Title = "Lots and serial numbers", ReadOnly = true)]
    [Description("Reads lots and serial numbers as their screens show them: kind=lot with what=list (lots, by search or expiring by a day) or stock (what each place holds per lot; needs Lots); kind=serial with what=list (serial numbers, by product, holder or search), stock (needs Serials) or moves (one serial's moves; needs Serials).")]
    public Task<object> LotsAndSerialsAsync(
        [Description("lot or serial.")] string kind,
        [Description("list, stock or moves.")] string what,
        [Description("Optional: a search text.")] string? search = null,
        [Description("lot list: only lots expiring by this day.")] DateOnly? expiresBy = null,
        [Description("serial list: only this product (id or exact code).")] string? product = null,
        [Description("serial list: only at this place (id or exact code).")] string? place = null,
        [Description("serial moves: the serial's id.")] Guid? serial = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var isLot = kind.Trim().ToUpperInvariant() switch { "LOT" or "LOTS" => true, "SERIAL" or "SERIALS" => false, _ => throw new McpException("The kind is lot or serial.") };
            var w = what.Trim().ToUpperInvariant();
            if (isLot)
            {
                await Registers.Screen(visibility, "lots", ct).ConfigureAwait(false);
                return w switch
                {
                    "LIST" => await lots.ListAsync(search, expiresBy, Registers.English, ct).ConfigureAwait(false),
                    "STOCK" => (object)await lots.StockAsync(Registers.English, ct).ConfigureAwait(false),
                    _ => throw new McpException("For lots, what is list or stock."),
                };
            }

            if (w == "LIST")
            {
                var productId = product is null ? (Guid?)null : await ProductAsync(product, ct).ConfigureAwait(false);
                var placeId = place is null ? (Guid?)null : await PlaceAsync(place, ct).ConfigureAwait(false);
                return await serials.SerialsAsync(productId, placeId, search, Registers.English, ct).ConfigureAwait(false);
            }

            await Registers.Screen(visibility, "serials", ct).ConfigureAwait(false);
            return w switch
            {
                "STOCK" => await serials.StockAsync(Registers.English, ct).ConfigureAwait(false),
                "MOVES" => (object)await serials.MovesAsync(serial ?? throw new McpException("Give the serial's id."), Registers.English, ct).ConfigureAwait(false),
                _ => throw new McpException("For serials, what is list, stock or moves."),
            };
        });

    [McpServerTool(Name = "assign_lots_serials", Title = "Give stock a lot or serial numbers", ReadOnly = false, Destructive = false)]
    [Description("Gives stock already held at a place a lot (with its expiry date and quantity) or serial numbers, as the Lots and Serials screens' Assign does (stock that came in before the product was tracked). Needs the Lots or Serials screen. Show the user what will be assigned and get an explicit yes; then confirm=true.")]
    public Task<object> AssignLotsSerialsAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("lot or serial.")] string kind,
        [Description("The place that holds the stock: id or exact code.")] string place,
        [Description("The product: id or exact code.")] string product,
        [Description("Optional: the unit (id, code or name); the product's default when left out.")] string? unit = null,
        [Description("lot: the lot's number.")] string? lot = null,
        [Description("lot: optional, its expiry date.")] DateOnly? expiryDate = null,
        [Description("lot: the quantity in it.")] decimal? quantity = null,
        [Description("serial: the serial numbers.")] IReadOnlyList<string>? serialNumbers = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the lot or serial numbers to assign");
            Erp.NeedsPlace(request);
            var placeId = await PlaceAsync(place, ct).ConfigureAwait(false);
            var productId = await ProductAsync(product, ct).ConfigureAwait(false);
            var unitId = await UnitAsync(productId, unit, ct).ConfigureAwait(false);
            switch (kind.Trim().ToUpperInvariant())
            {
                case "LOT" or "LOTS":
                    await Registers.Screen(visibility, "lots", ct).ConfigureAwait(false);
                    await lots.AssignAsync(new AssignLotRequest(placeId, productId, unitId, lot ?? throw new McpException("Give the lot's number."), expiryDate,
                        quantity ?? throw new McpException("Give the quantity.")), ct).ConfigureAwait(false);
                    return new { assigned = true, lot, quantity };
                case "SERIAL" or "SERIALS":
                    await Registers.Screen(visibility, "serials", ct).ConfigureAwait(false);
                    var numbers = serialNumbers is { Count: > 0 } given ? given : throw new McpException("Give the serial numbers.");
                    return await serials.AssignAsync(new AssignSerialsRequest(placeId, productId, unitId, [.. numbers.Select(n => new TransactionSerialDto(n.Trim()))]), Registers.English, ct)
                        .ConfigureAwait(false);
                default:
                    throw new McpException("The kind is lot or serial.");
            }
        });

    // ---------- Bank statements ----------

    [McpServerTool(Name = "bank_statements", Title = "Bank statements and their lines", ReadOnly = true)]
    [Description("Reads bank statements as their screen shows them: with nothing given, the imported statements; with a statement id, its lines (date, text, reference, amount, the document matched or ignored); with a line id, what it can be matched to (open documents) and the document types a new one could be. Statements are imported as a file on the Bank statements screen. Needs the Bank statements screen.")]
    public Task<object> BankStatementsAsync(
        [Description("Optional: a statement's id, for its lines.")] Guid? statement = null,
        [Description("Optional: a line's id, for what it can be matched to.")] Guid? line = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "bank-statements", ct).ConfigureAwait(false);
            if (line is { } l)
            {
                return await statements.ChoicesAsync(l, Registers.English, ct).ConfigureAwait(false);
            }

            return statement is { } s ? await statements.LinesAsync(s, ct).ConfigureAwait(false) : await statements.ListAsync(Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "decide_statement_line", Title = "Match, ignore or clear a statement line", ReadOnly = false, Destructive = false)]
    [Description("Decides a bank statement line by hand, as the screen does: action match (to a posted money document, by its id; bank_statements with the line lists them), ignore (nothing to book), clear (undo the decision), or remove (switches the whole statement off, by its id). Needs the Bank statements screen. Show the user the line and the decision and get an explicit yes; then confirm=true.")]
    public Task<object> DecideStatementLineAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("match, ignore, clear or remove.")] string action,
        [Description("The line's id (match, ignore, clear) or the statement's id (remove).")] Guid id,
        [Description("match: the document's id.")] Guid? document = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the decision on the statement line");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "bank-statements", ct).ConfigureAwait(false);
            switch (action.Trim().ToUpperInvariant())
            {
                case "MATCH":
                    await statements.MatchAsync(id, document ?? throw new McpException("Give the document to match."), ct).ConfigureAwait(false);
                    break;
                case "IGNORE":
                    await statements.IgnoreAsync(id, ct).ConfigureAwait(false);
                    break;
                case "CLEAR":
                    await statements.ClearAsync(id, ct).ConfigureAwait(false);
                    break;
                case "REMOVE":
                    await statements.RemoveAsync(id, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new McpException("The action is match, ignore, clear or remove.");
            }

            return new { done = action.Trim().ToLowerInvariant(), id, document };
        });

    // ---------- Repairs ----------

    [McpServerTool(Name = "repairs", Title = "The repairs board", ReadOnly = true)]
    [Description("The repairs board as its screen shows it: the stages, the cards (intake, client, item, technician, stage) and the technicians. Needs the Repairs screen.")]
    public Task<object> RepairsAsync(CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "repairs", ct).ConfigureAwait(false);
            return await repairs.BoardAsync(Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "repair", Title = "Work on a repair", ReadOnly = false, Destructive = false)]
    [Description("Works a repair as the board does: action new (an intake for a client, by partner or by name and phone, optionally with a technician), move (to a stage), technician (assign one, or none), offer (makes the offer for the client), bill (makes the bill), decide (the client accepted the offer or not). Needs the Repairs screen. Show the user the change and get an explicit yes; then confirm=true.")]
    public Task<object> RepairAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("new, move, technician, offer, bill or decide.")] string action,
        [Description("The card's id (repairs lists them); not for new.")] Guid? id = null,
        [Description("new: the client (id or exact code).")] string? partner = null,
        [Description("new: a client's name when not in the register.")] string? name = null,
        [Description("new: the client's phone.")] string? phone = null,
        [Description("new, technician: the technician (id or name); technician with none given takes the technician off.")] string? technician = null,
        [Description("move: the stage (id or name).")] string? stage = null,
        [Description("decide: true accepted, false refused.")] bool? accepted = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the change to the repair");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "repairs", ct).ConfigureAwait(false);
            var board = await repairs.BoardAsync(Registers.English, ct).ConfigureAwait(false);
            Guid? TechnicianOf(string? given) => string.IsNullOrWhiteSpace(given) ? null
                : board.Technicians.FirstOrDefault(t => Registers.Is(t.Id, given.Trim()) || string.Equals(t.Name, given.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
                  ?? throw new McpException($"No technician \"{given}\". Technicians: {string.Join(", ", board.Technicians.Select(t => t.Name))}.");
            var card = id ?? Guid.Empty;
            switch (action.Trim().ToUpperInvariant())
            {
                case "NEW":
                    var partnerId = partner is null ? (Guid?)null
                        : await Registers.ElementAsync(s => Registers.Partners(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), partner, "partner")
                            .ConfigureAwait(false);
                    return await repairs.NewAsync(new NewRepairRequest(partnerId, name, phone, TechnicianOf(technician)), ct).ConfigureAwait(false);
                case "MOVE":
                    var stageId = board.Stages.FirstOrDefault(s => Registers.Is(s.Id, stage ?? "") || string.Equals(s.Name, stage?.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
                        ?? throw new McpException($"Give the stage: {string.Join(", ", board.Stages.Select(s => s.Name))}.");
                    await repairs.MoveAsync(Card(card), stageId, ct).ConfigureAwait(false);
                    return new { moved = true, id = card, stage = stageId };
                case "TECHNICIAN":
                    var technicianId = TechnicianOf(technician);
                    await repairs.AssignAsync(Card(card), technicianId, ct).ConfigureAwait(false);
                    return new { id = card, technician = technicianId };
                case "OFFER":
                    return await repairs.OfferAsync(Card(card), ct).ConfigureAwait(false);
                case "BILL":
                    return await repairs.BillAsync(Card(card), ct).ConfigureAwait(false);
                case "DECIDE":
                    await repairs.DecideAsync(Card(card), accepted ?? throw new McpException("Say whether the client accepted (accepted=true or false)."), ct).ConfigureAwait(false);
                    return new { id = card, accepted };
                default:
                    throw new McpException("The action is new, move, technician, offer, bill or decide.");
            }
        });

    private static Guid Card(Guid id) => id == Guid.Empty ? throw new McpException("Give the repair card's id (repairs lists them).") : id;

    private async Task<Guid> BookAsync(string? given, CancellationToken ct)
    {
        var books = await depreciation.BooksAsync(Registers.English, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(given))
        {
            return books.Count == 1 ? books[0].Id : throw new McpException($"Give the book: {string.Join(", ", books.Select(b => b.Name))}.");
        }

        var key = given.Trim();
        var found = books.Where(b => Registers.Is(b.Id, key) || string.Equals(b.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count == 1 ? found[0].Id : throw new McpException($"No single book \"{key}\". Books: {string.Join(", ", books.Select(b => b.Name))}.");
    }

    private async Task<Guid> ProductAsync(string given, CancellationToken ct) =>
        await Registers.ElementAsync(s => Registers.Products(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), given, "product")
            .ConfigureAwait(false);

    private async Task<Guid> PlaceAsync(string given, CancellationToken ct) =>
        await Registers.ElementAsync(s => Registers.CostCenters(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), given, "cost centre")
            .ConfigureAwait(false);

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
}

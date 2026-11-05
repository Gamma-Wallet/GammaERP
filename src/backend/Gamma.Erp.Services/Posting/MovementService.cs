using System.Globalization;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gamma.Erp.Services.Posting;

/// <summary>
/// What posting a document does to balances and stock. Every rule is configuration:
/// <list type="bullet">
/// <item>Balances: the document type's from / to row (tbl_document_type_conf_from / _to) names the amount that goes into that side's balance:
/// credit on the from side, debit on the to side, for the line's own element on that side or else the document's.</item>
/// <item>Stock: lines whose transaction configuration moves stock (tbl_transaction_type_conf.stock_direction) take their quantity of the
/// specific product (product × unit) out of the from side and into the to side (1), out of the from side only (2, a raw material used) or into
/// the to side only (3, a produced item), or out of the from side only and not part of what is made (4, waste); a reversal moves a one-sided
/// line the other way. Where the company's setting says a kind of holder's stock may
/// not go negative (e.g. its warehouses), a line taking more than there is is refused (<see cref="ErrorCodes.StockError"/>, naming the places
/// that have it): the line then takes it from one of those.</item>
/// <item>Recipes: a product marked composite with ingredients (tbl_product_recipe, for the line's unit or for any) moves its ingredients' stock
/// instead of its own, each the line's quantity × its quantity, waste included.</item>
/// <item>Cost layers, first in, first out (<see cref="CostLayerBook"/>): the side row says whether the side takes its oldest layers, makes one
/// (a purchase bill), receives what the from side took, or puts back / takes off what a reversed line moved. The line keeps the cost it
/// moved (tbl_transaction.cost_lcu).</item>
/// <item>Product value: the side row names the amount that goes into the product's value: debit on the to side (with the purchase price
/// recorded), credit on the from side. When the amount is the line's stock cost, each product the line moved (each ingredient) gets its own.</item>
/// </list>
/// The balance and stock tables are found through the model by the side's entity (its table + "_balance", "tbl_inter_" + entity +
/// "_product_stock"), so no entity is named in code.
/// </summary>
public sealed class MovementService(ErpDbContext db)
{
    /// <summary>The stock a draft would move, checked; its serial numbers wait for posting, a draft may not have them yet.</summary>
    public Task<Movements> PlanAsync(Document document, IReadOnlyList<Transaction> transactions, CancellationToken ct) => PlanAsync(document, transactions, false, ct);

    /// <summary>The changes posting the document makes, serial numbers too, checked; refuses with every stock problem at once.</summary>
    public Task<Movements> PostingPlanAsync(Document document, IReadOnlyList<Transaction> transactions, CancellationToken ct) => PlanAsync(document, transactions, true, ct);

    private async Task<Movements> PlanAsync(Document document, IReadOnlyList<Transaction> transactions, bool withSerials, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transactions);
        var sides = new List<(bool IsFrom, Elements Entity, Guid? Balance, Guid? ProductValue, bool Checks, int Layers)>();
        foreach (var isFrom in new[] { true, false })
        {
            var row = isFrom
                ? await (from c in db.Set<DocumentTypeConfFrom>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                         where c.DocumentTypeId == document.TypeId select new { Entity = e, c.BalanceValueElementsId, c.ProductValueElementsId, c.CostLayers }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                : await (from c in db.Set<DocumentTypeConfTo>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                         where c.DocumentTypeId == document.TypeId select new { Entity = e, c.BalanceValueElementsId, c.ProductValueElementsId, c.CostLayers }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (row is not null)
            {
                // Whether this side's stock may go negative is the company's setting for the side's entity (empty = it may).
                var allowed = await NegativeStockAllowedAsync(document.CompanyId, row.Entity.Table, ct).ConfigureAwait(false);
                sides.Add((isFrom, row.Entity, row.BalanceValueElementsId, row.ProductValueElementsId, Checks: !allowed, row.CostLayers));
            }
        }

        var amounts = await new PostingService(db).AmountReadersAsync(
            sides.SelectMany(s => new[] { s.Balance, s.ProductValue }).Where(a => a is not null).Select(a => a!.Value), ct).ConfigureAwait(false);
        var confIds = transactions.Where(t => t.TypeConfId is not null).Select(t => t.TypeConfId!.Value).Distinct().ToList();
        var directions = await db.Set<TransactionTypeConf>().AsNoTracking().Where(c => confIds.Contains(c.Id) && c.StockDirection >= 1 && c.StockDirection <= ValueOnly)
            .ToDictionaryAsync(c => c.Id, c => c.StockDirection, ct).ConfigureAwait(false);
        // A value-only line (a disassembly's waste) moves no stock: it carries its share of the cost to an expense.
        var movesStock = directions.Where(d => d.Value != ValueOnly).Select(d => d.Key).ToList();
        // A reversal undoes its lines: what was used comes back, what was produced goes.
        var isReversal = await db.Set<DocumentType>().AsNoTracking().AnyAsync(d => d.Id == document.TypeId && d.IsReversal, ct).ConfigureAwait(false);
        int Direction(Transaction t) => t.TypeConfId is { } c && directions.TryGetValue(c, out var d) ? d : 0;
        bool OutOnly(Transaction t) => isReversal ? Direction(t) == 3 : Direction(t) is 2 or 4;
        bool InOnly(Transaction t) => isReversal ? Direction(t) is 2 or 4 : Direction(t) == 3;

        // A composite product with a recipe gives up its ingredients instead of itself, one level deep.
        var stockProducts = transactions.Where(t => t.TypeConfId is { } c && movesStock.Contains(c) && t.ElementId is not null).Select(t => t.ElementId!.Value).Distinct().ToList();
        var composite = await db.Set<Product>().AsNoTracking().Where(p => stockProducts.Contains(p.Id) && p.IsComposite).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        var recipes = (await db.Set<ProductRecipe>().AsNoTracking().Where(r => composite.Contains(r.ProductId) && r.Quantity > 0).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(r => r.ProductId);
        var ingredientIds = recipes.SelectMany(g => g).Select(r => r.ComponentId).Distinct().ToList();
        var ingredientNames = await db.Set<Product>().AsNoTracking().Where(p => ingredientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name.In(TranslatedText.DefaultLanguage), ct)
            .ConfigureAwait(false);
        // What a production point produced came into stock as itself: a type that says so sells it as itself, not its ingredients again.
        var sellsProduced = await db.Set<DocumentType>().AsNoTracking().AnyAsync(d => d.Id == document.TypeId && d.SellsProducedProduct, ct).ConfigureAwait(false);
        var fromLines = sellsProduced ? transactions.Select(t => t.IdSup).OfType<Guid>().ToList() : [];
        var producedLines = (await db.Set<ProducedLine>().AsNoTracking().Where(p => p.Active && fromLines.Contains(p.BillLineId)).Select(p => p.BillLineId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        IEnumerable<(Guid Product, Guid Unit, decimal Quantity, string? Name)> Stocked(Transaction t)
        {
            // Only what is sold or moved gives up its ingredients: a raw material used or a product made is itself.
            var rows = Direction(t) != 1 || (t.IdSup is { } source && producedLines.Contains(source)) ? [] : recipes[t.ElementId!.Value].Where(r => r.UnitId == t.UnitId).ToList() is { Count: > 0 } own ? own : [.. recipes[t.ElementId!.Value].Where(r => r.UnitId == null)];
            return rows.Count == 0
                ? [(t.ElementId!.Value, t.UnitId!.Value, t.Quantity ?? 0, t.ElementName)]
                : rows.Select(r => (r.ComponentId, r.ComponentUnitId, (t.Quantity ?? 0) * r.Quantity * (1 + r.WastePercent / 100), ingredientNames.GetValueOrDefault(r.ComponentId)));
        }

        // The "Transaction amount" rows that read the line's stock cost: product value from them is each moved product's own cost.
        var costColumn = db.Model.FindEntityType(typeof(Transaction))!.FindProperty(nameof(Transaction.CostLcu))!.GetColumnName();
        var costAmounts = await db.Set<Elements>().AsNoTracking().Where(e => e.Record == costColumn).Select(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
        var book = new CostLayerBook(db, document);
        var taken = new Dictionary<(int Line, Guid Product, Guid Unit), List<CostLayerBook.Piece>>();
        var plan = new Movements(db, document, book);
        await ElementBalancesAsync(plan, document, transactions, ct).ConfigureAwait(false);
        // A production shares what its raw materials cost among its products, by quantity.
        var produced = transactions.Where(t => InOnly(t) && t.ElementId is not null && t.UnitId is not null).Sum(t => t.Quantity ?? 0);
        var (usedShared, producedShared) = (0m, 0m);
        // A disassembly shares what the product taken apart cost among what comes back and what is lost, by quantity times average cost.
        Dictionary<int, decimal>? shares = null;
        async Task<Dictionary<int, decimal>> SharesAsync(Guid holder)
        {
            var lines = transactions.Select((t, i) => (t, i)).Where(x => Direction(x.t) is 3 or ValueOnly && x.t.ElementId is not null && x.t.UnitId is not null).ToList();
            var weights = new List<decimal>();
            foreach (var (t, _) in lines)
            {
                weights.Add((t.Quantity ?? 0) * await AverageCostAsync(document.CompanyId, t.ToId ?? holder, t.ElementId!.Value, t.UnitId!.Value, ct).ConfigureAwait(false));
            }

            if (weights.Sum() <= 0)
            {
                weights = [.. lines.Select(x => x.t.Quantity ?? 0)];
            }

            var (total, sum, given, result) = (taken.Values.SelectMany(p => p).Sum(p => p.Value), weights.Sum(), 0m, new Dictionary<int, decimal>());
            foreach (var ((_, i), k) in lines.Select((x, k) => (x, k)))
            {
                var value = k == lines.Count - 1 || sum == 0 ? total - given : Math.Round(total * weights[k] / sum, 2, MidpointRounding.AwayFromZero);
                given += value;
                result[i] = value;
            }

            return result;
        }

        var errors = new Dictionary<string, string>();
        var stock = new Dictionary<(IEntityType Table, Guid Holder, Guid Product, Guid Unit), (decimal Change, List<int> Lines, string? Name, bool Checks)>();
        foreach (var (isFrom, entity, balance, productValue, checks, layers) in sides)
        {
            var element = ElementOf(entity.Table);
            if (element is null)
            {
                continue;
            }

            var balanceTable = Find($"{entity.Table}_balance");
            var stockTable = Find($"tbl_inter_{entity.Table![4..]}_product_stock");
            foreach (var (t, i) in transactions.Select((t, i) => (t, i)))
            {
                if ((isFrom ? t.FromId ?? document.FromId : t.ToId ?? document.ToId) is not { } holder)
                {
                    continue;
                }

                var rate = t.ExchangeRate is > 0 ? t.ExchangeRate.Value : 1;
                if (balance is { } b && balanceTable is not null && amounts.TryGetValue(b, out var readBalance))
                {
                    var lcu = readBalance(t);
                    plan.Balance(balanceTable, element, holder, t, isFrom ? 0 : lcu / rate, isFrom ? lcu / rate : 0, isFrom ? 0 : lcu, isFrom ? lcu : 0);
                }

                if (!isFrom && layers == CostLayerBook.ShareTaken && Direction(t) == ValueOnly)
                {
                    shares ??= await SharesAsync(holder).ConfigureAwait(false);
                    t.CostLcu = shares.GetValueOrDefault(i);
                    continue;
                }

                var isProduct = t.TypeConfId is { } conf && movesStock.Contains(conf) && t.ElementId is not null && t.UnitId is not null;
                if (!isProduct || (OutOnly(t) && !isFrom) || (InOnly(t) && isFrom))
                {
                    continue;
                }

                if (stockTable is not null)
                {
                    foreach (var (product, unit, quantity, name) in Stocked(t))
                    {
                        var key = (stockTable, holder, product, unit);
                        var (change, lines, _, _) = stock.GetValueOrDefault(key, (0m, [], null, false));
                        lines.Add(i);
                        stock[key] = (change + (isFrom ? -1 : 1) * quantity, lines, name, checks);
                    }
                }

                var costs = new List<(Guid Product, Guid Unit, decimal Value)>();
                if (layers != CostLayerBook.None)
                {
                    var made = productValue is { } mv && amounts.TryGetValue(mv, out var readMade) && !costAmounts.Contains(mv) ? readMade(t) : (decimal?)null;
                    if (layers == CostLayerBook.Make && made is null)
                    {
                        errors[$"transactions[{i}].stock"] = $"{ErrorCodes.NotConfiguredError}: the side makes cost layers but names no product value for them.";
                        continue;
                    }

                    var items = Stocked(t).ToList();
                    var (total, shared) = (items.Sum(x => x.Quantity), 0m);
                    foreach (var ((product, unit, quantity, name), k) in items.Select((x, k) => (x, k)))
                    {
                        var share = k == items.Count - 1 || total == 0 ? (made ?? 0) - shared : Math.Round((made ?? 0) * quantity / total, 2, MidpointRounding.AwayFromZero);
                        shared += share;
                        List<CostLayerBook.Piece> pieces;
                        string? error = null;
                        switch ((isFrom, layers))
                        {
                            case (true, CostLayerBook.Take):
                                (pieces, error) = await book.TakeAsync(holder, product, unit, quantity, t, name, ct).ConfigureAwait(false);
                                break;
                            case (true, CostLayerBook.TakeOff):
                                (pieces, error) = await book.TakeOffAsync(holder, product, unit, quantity, t, name, ct).ConfigureAwait(false);
                                break;
                            case (false, CostLayerBook.Make):
                                pieces = await book.BringAsync(holder, product, unit, [book.Bought(quantity, share)], t, 0, ct).ConfigureAwait(false);
                                break;
                            case (false, CostLayerBook.Receive) when taken.TryGetValue((i, product, unit), out var carried):
                                pieces = await book.BringAsync(holder, product, unit, carried, t, 0, ct).ConfigureAwait(false);
                                break;
                            case (false, CostLayerBook.Receive):
                                (pieces, error) = ([], $"{ErrorCodes.StockError}: {name} comes in only with a purchase bill or from another place of the company.");
                                break;
                            case (false, CostLayerBook.MakeFromUsed):
                            {
                                // Waste (4) is an expense, not part of what is made.
                                var used = taken.Where(x => Direction(transactions[x.Key.Line]) != 4).SelectMany(x => x.Value).Sum(x => x.Value);
                                producedShared += quantity;
                                var value = producedShared >= produced ? used - usedShared : Math.Round(used * quantity / produced, 2, MidpointRounding.AwayFromZero);
                                usedShared += value;
                                pieces = await book.BringAsync(holder, product, unit, [book.Bought(quantity, value)], t, 0, ct).ConfigureAwait(false);
                                break;
                            }

                            case (false, CostLayerBook.ShareTaken):
                                shares ??= await SharesAsync(holder).ConfigureAwait(false);
                                pieces = await book.BringAsync(holder, product, unit, [book.Bought(quantity, shares.GetValueOrDefault(i))], t, 0, ct).ConfigureAwait(false);
                                break;
                            case (false, CostLayerBook.ReceiveConverted):
                                (pieces, error) = await ConvertedAsync(taken, product, unit, quantity, name, ct).ConfigureAwait(false);
                                if (error is null)
                                {
                                    pieces = await book.BringAsync(holder, product, unit, pieces, t, 0, ct).ConfigureAwait(false);
                                }

                                break;
                            case (false, CostLayerBook.PutBack):
                                (pieces, error) = await book.PutBackAsync(holder, product, unit, quantity, t, name, ct).ConfigureAwait(false);
                                break;
                            default:
                                pieces = [];
                                break;
                        }

                        if (error is not null)
                        {
                            errors[$"transactions[{i}].stock"] = error;
                            continue;
                        }

                        if (isFrom)
                        {
                            taken[(i, product, unit)] = pieces;
                        }

                        costs.Add((product, unit, pieces.Sum(p => p.Value)));
                    }

                    // The line's cost is what its from side took; a purchase's or a product's is what it brought.
                    if (costs.Count > 0 && (isFrom || !taken.Keys.Any(k => k.Line == i)))
                    {
                        t.CostLcu = costs.Sum(c => c.Value);
                    }
                }

                if (productValue is { } pv && amounts.TryGetValue(pv, out var readValue))
                {
                    if (costAmounts.Contains(pv) && costs.Count > 0)
                    {
                        foreach (var (product, unit, cost) in costs)
                        {
                            plan.ProductValue(t, product, unit, isFrom ? 0 : cost / rate, isFrom ? cost / rate : 0, isFrom ? 0 : cost, isFrom ? cost : 0, recordPrice: false);
                        }
                    }
                    else
                    {
                        var lcu = readValue(t);
                        plan.ProductValue(t, t.ElementId!.Value, t.UnitId!.Value, isFrom ? 0 : lcu / rate, isFrom ? lcu / rate : 0, isFrom ? 0 : lcu, isFrom ? lcu : 0,
                            recordPrice: !isFrom);
                    }
                }
            }
        }

        foreach (var ((table, holder, product, unit), (change, lines, name, checks)) in stock)
        {
            var specific = await db.Set<InterProductSpecificUnit>().AsNoTracking().Where(s => s.ProductId == product && s.UnitId == unit).Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            var holderColumn = HolderProperty(table);
            var rows = specific is null ? [] : await StockRowsAsync(table, specific.Value, ct).ConfigureAwait(false);
            var current = rows.Where(r => Equals(Get(r, holderColumn), holder)).Sum(r => (decimal?)Get(r, "Quantity") ?? 0);
            if (checks && current + change < 0)
            {
                var elsewhere = rows.Where(r => !Equals(Get(r, holderColumn), holder) && ((decimal?)Get(r, "Quantity") ?? 0) >= -change)
                    .Select(r => (Id: (Guid)Get(r, holderColumn)!, Quantity: (decimal?)Get(r, "Quantity") ?? 0)).ToList();
                var names = await NamesAsync(table, elsewhere.Select(e => e.Id), ct).ConfigureAwait(false);
                errors[$"transactions[{lines[0]}].stock"] = $"{ErrorCodes.StockError}: there are {current.ToString("0.###", CultureInfo.InvariantCulture)} of {name}, " +
                    $"{(-change).ToString("0.###", CultureInfo.InvariantCulture)} are needed; " +
                    (elsewhere.Count == 0 ? "no other place has them." : "take them from " + string.Join(", ",
                        elsewhere.Select(e => $"{names.GetValueOrDefault(e.Id, e.Id.ToString())} ({e.Quantity.ToString("0.###", CultureInfo.InvariantCulture)})")) + ".");
                continue;
            }

            plan.Stock(table, holderColumn, holder, product, unit, specific, change);
        }

        // An owed layer settled at another cost than it was valued at leaves the difference in the product's value.
        foreach (var (product, unit, difference) in book.Settlements)
        {
            plan.Settlement(product, unit, difference);
        }

        if (withSerials)
        {
            await SerialsAsync().ConfigureAwait(false);
            await LotsAsync().ConfigureAwait(false);
        }

        return errors.Count == 0 ? plan : throw new ValidationException(errors);

        // A product that tracks serial numbers moves unit by unit: one number per unit on its line, each from who holds it to the other side. A number
        // comes new only from a partner (a purchase); a place gives up only what it holds.
        async Task SerialsAsync()
        {
            var tracked = (await (from p in db.Set<Product>().AsNoTracking() join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                                  where stockProducts.Contains(p.Id) && k.TracksSerial && !p.IsComposite select p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
            if (tracked.Count == 0)
            {
                return;
            }

            var lineIds = transactions.Select(t => t.Id).ToList();
            var numbers = (await db.Set<TransactionSerial>().Where(s => lineIds.Contains(s.TransactionId)).OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false)).ToLookup(s => s.TransactionId);
            var (fromTable, toTable) = (sides.Where(s => s.IsFrom).Select(s => s.Entity.Table).FirstOrDefault(), sides.Where(s => !s.IsFrom).Select(s => s.Entity.Table).FirstOrDefault());
            var partnerTable = db.Model.FindEntityType(typeof(Models.Partners.Partner))!.GetTableName();
            var seen = new HashSet<(Guid Product, string Serial)>();
            foreach (var (t, i) in transactions.Select((t, i) => (t, i)))
            {
                if (t.ElementId is not { } product || !tracked.Contains(product) || t.UnitId is null || Direction(t) is 0 or ValueOnly)
                {
                    continue;
                }

                var rows = numbers[t.Id].ToList();
                if (rows.Count != (t.Quantity ?? 0))
                {
                    errors[$"transactions[{i}].serials"] = $"{ErrorCodes.StockError}: {t.ElementName} tracks serial numbers: {(t.Quantity ?? 0).ToString("0.###", CultureInfo.InvariantCulture)} are needed, " +
                        $"{rows.Count} are given. Give them on an invoice's line; a point of sale can't take them.";
                    continue;
                }

                var from = !InOnly(t) && fromTable is not null ? t.FromId ?? document.FromId : null;
                var to = !OutOnly(t) && toTable is not null ? t.ToId ?? document.ToId : null;
                foreach (var row in rows)
                {
                    if (!seen.Add((product, row.Serial)))
                    {
                        errors[$"transactions[{i}].serials"] = $"{ErrorCodes.StockError}: serial number {row.Serial} is given twice.";
                        continue;
                    }

                    var known = await db.Set<ProductSerial>().FirstOrDefaultAsync(s => s.CompanyId == document.CompanyId && s.ProductId == product && s.Serial == row.Serial, ct).ConfigureAwait(false);
                    var refused = known is null ? from is not null && fromTable != partnerTable ? $"serial number {row.Serial} of {t.ElementName} is not here."
                            : null
                        : from is null ? $"serial number {row.Serial} of {t.ElementName} already exists."
                        : known.HolderId != from ? $"serial number {row.Serial} of {t.ElementName} is held elsewhere." : null;
                    if (refused is not null)
                    {
                        errors[$"transactions[{i}].serials"] = $"{ErrorCodes.StockError}: {refused}";
                        continue;
                    }

                    plan.Serial(row, known, product, to is null ? null : toTable, to);
                }
            }
        }

        // A product that tracks lots moves lot by lot: a line names its lot, a place gives up only what it holds of it, a lot comes new from outside. A partner's balance is kept
        // to show who has what, but a supplier holds none, so a partner is never refused. Production and conversion use stock nobody picks a lot for, so they are refused
        // for now (the founder, 2026-11-02).
        async Task LotsAsync()
        {
            var tracked = await (from p in db.Set<Product>().AsNoTracking() join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                                 where (stockProducts.Contains(p.Id) || ingredientIds.Contains(p.Id)) && k.TracksLot && !p.IsComposite
                                 select new { p.Id, k.RefusesExpired }).ToDictionaryAsync(p => p.Id, p => p.RefusesExpired, ct).ConfigureAwait(false);
            if (tracked.Count == 0)
            {
                return;
            }

            var (fromTable, toTable) = (sides.Where(s => s.IsFrom).Select(s => s.Entity.Table).FirstOrDefault(), sides.Where(s => !s.IsFrom).Select(s => s.Entity.Table).FirstOrDefault());
            var partnerTable = db.Model.FindEntityType(typeof(Models.Partners.Partner))!.GetTableName();
            var converts = sides.Any(s => s.Layers is CostLayerBook.MakeFromUsed or CostLayerBook.ReceiveConverted or CostLayerBook.ShareTaken);
            var day = document.PostingDate ?? document.DocumentDate;
            var used = new Dictionary<(Guid Lot, Guid Unit, Guid Holder), decimal>();
            foreach (var (t, i) in transactions.Select((t, i) => (t, i)))
            {
                if (t.ElementId is not { } product || t.UnitId is not { } unit || Direction(t) is 0 or ValueOnly)
                {
                    continue;
                }

                if (!tracked.TryGetValue(product, out var refusesExpired))
                {
                    if (Stocked(t).FirstOrDefault(x => tracked.ContainsKey(x.Product)) is { Name: var ingredient, Product: var found } && found != Guid.Empty)
                    {
                        errors[$"transactions[{i}].lot"] = $"{ErrorCodes.StockError}: {ingredient} tracks lots and can't be used in a recipe.";
                    }

                    continue;
                }

                if (converts || Direction(t) is 2 or 4)
                {
                    errors[$"transactions[{i}].lot"] = $"{ErrorCodes.StockError}: {t.ElementName} tracks lots and can't go through production or a stock conversion.";
                    continue;
                }

                if (string.IsNullOrWhiteSpace(t.Lot))
                {
                    errors[$"transactions[{i}].lot"] = $"{ErrorCodes.StockError}: {t.ElementName} tracks lots: give its lot on the line.";
                    continue;
                }

                var number = t.Lot.Trim();
                var quantity = t.Quantity ?? 0;
                var known = await db.Set<ProductLot>().AsNoTracking().FirstOrDefaultAsync(l => l.Active && l.CompanyId == document.CompanyId && l.ProductId == product && l.Lot == number, ct).ConfigureAwait(false);
                if (known?.ExpiryDate is { } expiry && t.LotExpiryDate is { } typed && typed != expiry)
                {
                    errors[$"transactions[{i}].lot"] = $"{ErrorCodes.StockError}: lot {number} of {t.ElementName} expires on {expiry:yyyy-MM-dd}.";
                    continue;
                }

                if (!isReversal && toTable == partnerTable && refusesExpired && known?.ExpiryDate < day)
                {
                    errors[$"transactions[{i}].lot"] = $"{ErrorCodes.StockError}: lot {number} of {t.ElementName} expired on {known!.ExpiryDate:yyyy-MM-dd}.";
                    continue;
                }

                var from = !InOnly(t) && fromTable is not null ? t.FromId ?? document.FromId : null;
                var to = !OutOnly(t) && toTable is not null ? t.ToId ?? document.ToId : null;
                var taken = 0m;
                if (from is { } holder)
                {
                    var held = known is null ? 0 : await db.Set<ProductLotStock>().AsNoTracking().Where(s => s.Active && s.ProductLotId == known.Id && s.UnitId == unit && s.HolderId == holder)
                        .SumAsync(s => s.Quantity, ct).ConfigureAwait(false);
                    held -= known is null ? 0 : used.GetValueOrDefault((known.Id, unit, holder));
                    if (fromTable != partnerTable && held < quantity)
                    {
                        errors[$"transactions[{i}].lot"] = $"{ErrorCodes.StockError}: there are {held.ToString("0.###", CultureInfo.InvariantCulture)} of lot {number} of {t.ElementName} here, " +
                            $"{quantity.ToString("0.###", CultureInfo.InvariantCulture)} are needed.";
                        continue;
                    }

                    taken = Math.Min(held, quantity);
                    if (known is not null)
                    {
                        used[(known.Id, unit, holder)] = used.GetValueOrDefault((known.Id, unit, holder)) + taken;
                    }
                }

                plan.Lot(t, product, unit, number, t.LotExpiryDate, from is null ? null : fromTable, from, taken, to is null ? null : toTable, to, quantity);
            }
        }
    }

    /// <summary>
    /// A unit split's product: what the from side took of the same product in another unit, each layer on its own, its quantity turned into
    /// this unit by the conversion (tbl_unit_conversion: the product's own first, either way round) and its value kept, so its unit cost
    /// changes by the same factor (the founder, 2026-10-30). Refused unless the quantity is exactly the converted one.
    /// </summary>
    private async Task<(List<CostLayerBook.Piece> Pieces, string? Error)> ConvertedAsync(Dictionary<(int Line, Guid Product, Guid Unit), List<CostLayerBook.Piece>> taken,
        Guid product, Guid unit, decimal quantity, string? name, CancellationToken ct)
    {
        var from = taken.Where(x => x.Key.Product == product && x.Key.Unit != unit).ToList();
        if (from.Select(x => x.Key.Unit).Distinct().Count() != 1)
        {
            return ([], $"{ErrorCodes.StockError}: a unit split turns {name} in one other unit into this one; the document has {from.Count} lines of it in other units.");
        }

        var source = from[0].Key.Unit;
        var rows = await db.Set<Models.Units.UnitConversion>().AsNoTracking()
            .Where(c => ((c.FromUnitId == source && c.ToUnitId == unit) || (c.FromUnitId == unit && c.ToUnitId == source)) && (c.ProductId == null || c.ProductId == product) && c.Factor > 0)
            .ToListAsync(ct).ConfigureAwait(false);
        var row = rows.OrderBy(c => c.ProductId == null).FirstOrDefault();
        if (row is null)
        {
            return ([], $"{ErrorCodes.NotConfiguredError}: no conversion between the units of {name}.");
        }

        var factor = row.FromUnitId == source ? row.Factor : 1 / row.Factor;
        var pieces = from.SelectMany(x => x.Value).ToList();
        var converted = Math.Round(pieces.Sum(p => p.Quantity) * factor, 6, MidpointRounding.AwayFromZero);
        return converted != Math.Round(quantity, 6, MidpointRounding.AwayFromZero)
            ? ([], $"{ErrorCodes.StockError}: {pieces.Sum(p => p.Quantity).ToString("0.###", CultureInfo.InvariantCulture)} of {name} make " +
                $"{converted.ToString("0.###", CultureInfo.InvariantCulture)} in this unit, not {quantity.ToString("0.###", CultureInfo.InvariantCulture)}.")
            : ([.. pieces.Select(p => p with { Quantity = Math.Round(p.Quantity * factor, 6, MidpointRounding.AwayFromZero), Into = null })], null);
    }

    /// <summary>
    /// The start of the English name of the company properties (Company cost-centre type) that say whether an entity's stock may go negative,
    /// e.g. "Negative stock allowed (partners)"; each points at its entity, which tells them apart.
    /// </summary>
    public const string NegativeStockCode = "NEGATIVE_STOCK";

    /// <summary>
    /// The company's setting for an entity's stock (e.g. its warehouses, its partners): its value of the <see cref="NegativeStockCode"/>
    /// property whose elements row is that entity's table. 0 = may not go negative; 1 or empty = may.
    /// </summary>
    private async Task<bool> NegativeStockAllowedAsync(Guid company, string? table, CancellationToken ct)
    {
        var values = await (from v in db.Set<Models.CostCenters.CostCenterInfo>().AsNoTracking()
                            join p in db.Set<Models.CostCenters.CostCenterTypeInfo>().AsNoTracking() on v.TypeInfoId equals p.Id
                            join e in db.Set<Elements>().AsNoTracking() on p.ElementsId equals e.Id
                            where v.ElementId == company && p.Kind == PropertyKind.Number && e.Table == table
                            && p.Code == NegativeStockCode
                            select new { v.Value }).ToListAsync(ct).ConfigureAwait(false);
        var value = values.FirstOrDefault()?.Value;
        return value is null || value.GetValue<decimal>() != 0;
    }

    private IEntityType? Find(string table) => db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table);

    /// <summary>
    /// A line's amounts on its element's own balance:
    /// the document type × line type names which amounts, the line's scope which entity. Lines of a type naming none move nothing here.
    /// </summary>
    private async Task ElementBalancesAsync(Movements plan, Document document, IReadOnlyList<Transaction> transactions, CancellationToken ct)
    {
        var links = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking().Where(l => l.DocumentTypeId == document.TypeId && l.TransactionTypeId != null
            && (l.ElementDebitValueElementsId != null || l.ElementCreditValueElementsId != null)).ToListAsync(ct).ConfigureAwait(false);
        if (links.Count == 0)
        {
            return;
        }

        var amounts = await new PostingService(db).AmountReadersAsync(links.SelectMany(l => new[] { l.ElementDebitValueElementsId, l.ElementCreditValueElementsId }).OfType<Guid>(), ct)
            .ConfigureAwait(false);
        var confIds = transactions.Where(t => t.TypeConfId is not null).Select(t => t.TypeConfId!.Value).Distinct().ToList();
        var tables = await (from c in db.Set<TransactionTypeConf>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                            where confIds.Contains(c.Id) select new { c.Id, e.Table }).ToDictionaryAsync(x => x.Id, x => x.Table, ct).ConfigureAwait(false);
        foreach (var t in transactions)
        {
            var link = links.FirstOrDefault(l => l.TransactionTypeId == t.TypeId);
            if (link is null || t.ElementId is not { } holder || t.TypeConfId is not { } conf || tables.GetValueOrDefault(conf) is not { } table
                || ElementOf(table) is not { } element || Find($"{table}_balance") is not { } balance)
            {
                continue;
            }

            decimal Read(Guid? value) => value is { } v && amounts.TryGetValue(v, out var read) ? read(t) : 0;
            var (debit, credit) = (Read(link.ElementDebitValueElementsId), Read(link.ElementCreditValueElementsId));
            var rate = t.ExchangeRate is > 0 ? t.ExchangeRate.Value : 1;
            if (debit != 0 || credit != 0)
            {
                plan.Balance(balance, element, holder, t, debit / rate, credit / rate, debit, credit);
            }
        }
    }

    private IEntityType? ElementOf(string? table) =>
        table is null ? null : db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table && typeof(Element).IsAssignableFrom(e.ClrType));

    /// <summary>The stock table's column naming who holds the stock: the one that is neither the document nor the product (e.g. cost_center_id).</summary>
    internal static string HolderProperty(IEntityType table) =>
        table.GetProperties().Select(p => p.Name).Single(n => n.EndsWith("Id", StringComparison.Ordinal) && n is not ("Id" or "TenantId" or "OwnerId" or "IdSup" or "IdChange"
            or "CreatedBy" or "CompanyId" or "DocumentId" or "ProductSpecificUnitId"));

    internal static object? Get(object row, string property) => row.GetType().GetProperty(property)!.GetValue(row);

    internal static void Set(object row, string property, object? value) => row.GetType().GetProperty(property)!.SetValue(row, value);

    private async Task<List<object>> StockRowsAsync(IEntityType table, Guid specific, CancellationToken ct)
    {
        // Only this product's stock rows, filtered in SQL.
        var set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(table.ClrType).Invoke(db, null)!;
        return await set.AsNoTracking().Where(r => EF.Property<Guid?>(r, "ProductSpecificUnitId") == specific).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEntityType stockTable, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var holder = stockTable.FindNavigation(HolderProperty(stockTable)[..^2])?.TargetEntityType
            ?? stockTable.GetForeignKeys().FirstOrDefault(f => f.Properties.Single().Name == HolderProperty(stockTable))?.PrincipalEntityType;
        if (holder is null || !typeof(Element).IsAssignableFrom(holder.ClrType))
        {
            return [];
        }

        var wanted = ids.ToHashSet();
        var set = (IQueryable<Element>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(holder.ClrType).Invoke(db, null)!;
        return (await set.AsNoTracking().Where(e => wanted.Contains(e.Id)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(e => e.Id, e => e.Code ?? e.Name.In(TranslatedText.DefaultLanguage) ?? e.Id.ToString());
    }
    /// <summary>The stock direction of a line that moves no stock and only carries a value (a disassembly's waste).</summary>
    public const int ValueOnly = 5;

    /// <summary>What one of a product in a unit costs now: the average of what the place holds, else of what the company holds, else the last cost known.</summary>
    private async Task<decimal> AverageCostAsync(Guid company, Guid holder, Guid product, Guid unit, CancellationToken ct)
    {
        var layers = await (from l in db.Set<ProductCostLayer>().AsNoTracking()
                            join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on l.ProductSpecificUnitId equals u.Id
                            where l.CompanyId == company && l.Active && u.ProductId == product && u.UnitId == unit
                            select new { l.HolderId, l.QuantityLeft, l.ValueLeftLcu, l.UnitCostLcu, l.LayerDate, l.CreatedOn }).ToListAsync(ct).ConfigureAwait(false);
        var here = layers.Where(l => l.HolderId == holder && l.QuantityLeft > 0).ToList();
        if (here.Sum(l => l.QuantityLeft) is > 0 and var q)
        {
            return here.Sum(l => l.ValueLeftLcu) / q;
        }

        var held = layers.Where(l => l.QuantityLeft > 0).ToList();
        if (held.Sum(l => l.QuantityLeft) is > 0 and var all)
        {
            return held.Sum(l => l.ValueLeftLcu) / all;
        }

        return layers.OrderByDescending(l => l.LayerDate).ThenByDescending(l => l.CreatedOn).Select(l => l.UnitCostLcu).FirstOrDefault();
    }
}

/// <summary>The balance, stock and product-value changes of one posting, applied in posting's own database transaction.</summary>
public sealed class Movements(ErpDbContext db, Document document, CostLayerBook? layers = null)
{
    private readonly List<Func<CancellationToken, Task>> _changes = [];

    internal void Balance(IEntityType table, IEntityType element, Guid holder, Transaction t, decimal debit, decimal credit, decimal debitLcu, decimal creditLcu) =>
        _changes.Add(_ =>
        {
            var row = Activator.CreateInstance(table.ClrType)!;
            MovementService.Set(row, nameof(Row.OwnerId), document.OwnerId);
            MovementService.Set(row, "CompanyId", document.CompanyId);
            MovementService.Set(row, $"{element.ClrType.Name}Id", holder);
            MovementService.Set(row, "DocumentId", document.Id);
            MovementService.Set(row, "CurrencyId", t.CurrencyId);
            MovementService.Set(row, "ExchangeRate", t.ExchangeRate);
            MovementService.Set(row, "Debit", debit);
            MovementService.Set(row, "Credit", credit);
            MovementService.Set(row, "DebitLcu", debitLcu);
            MovementService.Set(row, "CreditLcu", creditLcu);
            db.Add(row);
            return Task.CompletedTask;
        });

    internal void ProductValue(Transaction t, Guid product, Guid unit, decimal debit, decimal credit, decimal debitLcu, decimal creditLcu, bool recordPrice) =>
        _changes.Add(async ct =>
        {
            var specific = await SpecificAsync(product, unit, ct).ConfigureAwait(false);
            db.Add(new ProductBalance
            {
                OwnerId = document.OwnerId, CompanyId = document.CompanyId, ProductSpecificUnitId = specific, DocumentId = document.Id, CurrencyId = t.CurrencyId,
                ExchangeRate = t.ExchangeRate, Debit = debit, Credit = credit, DebitLcu = debitLcu, CreditLcu = creditLcu,
            });
            if (recordPrice)
            {
                db.Add(new ProductPurchasePrice
                {
                    OwnerId = document.OwnerId, CompanyId = document.CompanyId, ProductId = t.ElementId, ProductSpecificUnitId = specific, DocumentId = document.Id,
                    VatGroupId = t.VatGroupId, CurrencyId = t.CurrencyId, Value = t.Value, ExchangeRate = t.ExchangeRate, ValueLcu = t.ValueLcu,
                });
            }
        });

    /// <summary>A serial number goes to who now holds it: its register row, made the first time, and its line's link to it.</summary>
    internal void Serial(TransactionSerial line, ProductSerial? known, Guid product, string? holderTable, Guid? holder) =>
        _changes.Add(ct =>
        {
            var row = known;
            if (row is null)
            {
                row = new ProductSerial { CompanyId = document.CompanyId, ProductId = product, Serial = line.Serial, PartNumber = line.PartNumber, OwnerId = document.OwnerId };
                db.Add(row);
            }

            (row.HolderTable, row.HolderId) = (holderTable, holder);
            line.ProductSerialId = row.Id;
            return Task.CompletedTask;
        });

    /// <summary>A line's quantity of a lot goes from one holder to the other; the lot is made the first time.</summary>
    internal void Lot(Transaction t, Guid product, Guid unit, string number, DateOnly? expiry, string? fromTable, Guid? from, decimal taken, string? toTable, Guid? to, decimal quantity) =>
        _changes.Add(async ct =>
        {
            var lot = db.ChangeTracker.Entries<ProductLot>().Select(e => e.Entity).FirstOrDefault(l => l.Active && l.CompanyId == document.CompanyId && l.ProductId == product && l.Lot == number)
                ?? await db.Set<ProductLot>().FirstOrDefaultAsync(l => l.Active && l.CompanyId == document.CompanyId && l.ProductId == product && l.Lot == number, ct).ConfigureAwait(false);
            if (lot is null)
            {
                lot = new ProductLot { CompanyId = document.CompanyId, ProductId = product, Lot = number, OwnerId = document.OwnerId };
                db.Add(lot);
            }

            lot.ExpiryDate ??= expiry;
            t.ProductLotId = lot.Id;
            if (from is { } giver && taken != 0)
            {
                await LotStockAsync(lot, unit, fromTable, giver, -taken, ct).ConfigureAwait(false);
            }

            if (to is { } taker)
            {
                await LotStockAsync(lot, unit, toTable, taker, quantity, ct).ConfigureAwait(false);
            }
        });

    private async Task LotStockAsync(ProductLot lot, Guid unit, string? table, Guid holder, decimal change, CancellationToken ct)
    {
        var row = db.ChangeTracker.Entries<ProductLotStock>().Select(e => e.Entity).FirstOrDefault(s => s.Active && s.ProductLotId == lot.Id && s.UnitId == unit && s.HolderId == holder)
            ?? await db.Set<ProductLotStock>().FirstOrDefaultAsync(s => s.Active && s.ProductLotId == lot.Id && s.UnitId == unit && s.HolderId == holder, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new ProductLotStock { CompanyId = document.CompanyId, ProductLotId = lot.Id, UnitId = unit, HolderTable = table, HolderId = holder, OwnerId = document.OwnerId };
            db.Add(row);
        }

        row.Quantity += change;
    }

    internal void Settlement(Guid product, Guid unit, decimal difference) =>
        _changes.Add(async ct => db.Add(new ProductBalance
        {
            OwnerId = document.OwnerId, CompanyId = document.CompanyId, ProductSpecificUnitId = await SpecificAsync(product, unit, ct).ConfigureAwait(false), DocumentId = document.Id,
            ExchangeRate = 1, Debit = difference < 0 ? -difference : 0, Credit = difference > 0 ? difference : 0, DebitLcu = difference < 0 ? -difference : 0,
            CreditLcu = difference > 0 ? difference : 0,
        }));

    internal void Stock(IEntityType table, string holderColumn, Guid holder, Guid product, Guid unit, Guid? specific, decimal change) =>
        _changes.Add(async ct =>
        {
            var id = specific ?? await SpecificAsync(product, unit, ct).ConfigureAwait(false);
            var set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(table.ClrType).Invoke(db, null)!;
            var row = await set.FirstOrDefaultAsync(r => EF.Property<Guid?>(r, holderColumn) == holder && EF.Property<Guid?>(r, "ProductSpecificUnitId") == id, ct)
                .ConfigureAwait(false);
            if (row is null)
            {
                row = Activator.CreateInstance(table.ClrType)!;
                MovementService.Set(row, nameof(Row.OwnerId), document.OwnerId);
                MovementService.Set(row, "CompanyId", document.CompanyId);
                MovementService.Set(row, holderColumn, holder);
                MovementService.Set(row, "ProductSpecificUnitId", id);
                MovementService.Set(row, "Quantity", 0m);
                db.Add(row);
            }

            // The row keeps its id; the history interceptor keeps the stock as it was before as a disabled copy.
            MovementService.Set(row, "Quantity", ((decimal?)MovementService.Get(row, "Quantity") ?? 0) + change);
            MovementService.Set(row, "DocumentId", document.Id);
            MovementService.Set(row, "DocumentQuantity", change);
        });

    /// <summary>Writes the changes; called inside posting's database transaction, before it saves.</summary>
    public async Task ApplyAsync(CancellationToken ct)
    {
        foreach (var change in _changes)
        {
            await change(ct).ConfigureAwait(false);
        }

        if (layers is not null)
        {
            await layers.ApplyAsync(SpecificAsync, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The specific product (product × unit), made on first use.</summary>
    private async Task<Guid> SpecificAsync(Guid product, Guid unit, CancellationToken ct)
    {
        var local = db.ChangeTracker.Entries<InterProductSpecificUnit>().Select(e => e.Entity).FirstOrDefault(s => s.ProductId == product && s.UnitId == unit);
        var existing = local ?? await db.Set<InterProductSpecificUnit>().FirstOrDefaultAsync(s => s.ProductId == product && s.UnitId == unit, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        var made = new InterProductSpecificUnit { ProductId = product, UnitId = unit, OwnerId = document.OwnerId };
        db.Add(made);
        return made.Id;
    }
}

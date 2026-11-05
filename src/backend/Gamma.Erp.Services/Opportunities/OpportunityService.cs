using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Opportunities;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Opportunities;

using OpportunityEntities = EntityService<OpportunityCategory, OpportunityType, InterOpportunityTypeCategory, OpportunityTypeInfo, OpportunityTypeInfoChoice, Opportunity,
    OpportunityInfo, InterOpportunityCategory, OpportunityAccess>;

public sealed record OpportunityStageDto(Guid Id, string? Name, decimal Chance, int Outcome);

public sealed record OpportunityCardDto(Guid Id, string? Code, string? Name, string? Partner, decimal Amount, string? Currency, DateOnly? ExpectedClose, Guid? StageId,
    int Documents = 0, decimal Invoiced = 0);

/// <summary>
/// A document of an opportunity, as the Opportunities and documents report lists it: linked to it, or the reversal of one that is. An invoice is a posted
/// document whose type holds an open item (the invoice run's rule); a reversal's total counts against it. Totals are in the company's currency.
/// </summary>
public sealed record OpportunityDocumentRowDto(Guid OpportunityId, string? Opportunity, string? Stage, string? Partner, Guid DocumentId, string? Code, string? Type,
    DateOnly? Date, bool Posted, bool Invoice, decimal TotalLcu);

public sealed record OpportunityBoardDto(IReadOnlyList<OpportunityStageDto> Stages, IReadOnlyList<OpportunityCardDto> Cards);

/// <summary>What a form offers: the company's stages and currencies.</summary>
public sealed record OpportunityFormDto(IReadOnlyList<OpportunityStageDto> Stages, IReadOnlyList<OptionDto> Currencies);

/// <summary>A month's forecast in one currency: open opportunities at amount × chance, won ones in full, lost ones not at all. Currencies are never added together.</summary>
public sealed record OpportunityForecastDto(string Month, string? Currency, decimal Open, decimal Weighted, decimal Won, decimal Invoiced = 0);

/// <summary>The pipeline: a person moves each opportunity from stage to stage (the founder, 2026-11-02); nothing moves by itself. Every move is in the row's history.</summary>
public sealed class OpportunityService(ErpDbContext db, RequestContext request, Visibility visibility)
{
    private const int Won = 1, Lost = 2;

    public async Task<OpportunityFormDto> FormAsync(string language, CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var currencies = await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.OwnerId == company).OrderByDescending(c => c.IsBase).ThenBy(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
        return new OpportunityFormDto(await StagesAsync(language, ct).ConfigureAwait(false), [.. currencies.Select(c => new OptionDto(c.Id, c.Code, c.Code ?? ""))]);
    }

    public async Task<OpportunityBoardDto> BoardAsync(string language, CancellationToken ct)
    {
        var stages = await StagesAsync(language, ct).ConfigureAwait(false);
        var ids = stages.Select(s => (Guid?)s.Id).ToList();
        var seen = (await new OpportunityEntities(db, request, visibility).AllElementsAsync(ct).ConfigureAwait(false)).Where(e => e.Id is not null).Select(e => e.Id!.Value).ToHashSet();
        var rows = (await db.Set<Opportunity>().AsNoTracking().Where(o => o.Active && ids.Contains(o.StageId)).ToListAsync(ct).ConfigureAwait(false)).Where(o => seen.Contains(o.Id)).ToList();
        var (partners, currencies) = await NamesAsync(rows, language, ct).ConfigureAwait(false);
        // Each card's documents (the founder, 2026-11-04): how many are linked, and its posted invoices less their reversals, in the card's currency at each one's day.
        var documents = (await DocumentsOfAsync(rows.Select(o => o.Id).ToList(), language, ct).ConfigureAwait(false)).ToLookup(d => d.OpportunityId);
        var baseCurrency = await BaseCurrencyAsync(ct).ConfigureAwait(false);
        var invoiced = new Dictionary<Guid, decimal>();
        foreach (var o in rows)
        {
            var sum = 0m;
            foreach (var d in documents[o.Id].Where(d => d.Row.Invoice))
            {
                sum += baseCurrency is { } bc ? await Currencies.PriceConversion.ConvertAsync(db, d.Row.TotalLcu, bc, o.CurrencyId, bc, d.Day, ct).ConfigureAwait(false) ?? 0 : d.Row.TotalLcu;
            }

            invoiced[o.Id] = sum;
        }

        return new OpportunityBoardDto(stages, [.. rows.Select(o => new OpportunityCardDto(o.Id, o.Code, o.Name.In(language), o.PartnerId is { } p ? partners.GetValueOrDefault(p) : null, o.Amount ?? 0,
            o.CurrencyId is { } c ? currencies.GetValueOrDefault(c) : null, o.ExpectedClose, o.StageId, documents[o.Id].Count(d => !d.Reversal), invoiced[o.Id])).OrderBy(o => o.ExpectedClose)]);
    }

    /// <summary>Whether the user sees the opportunity (its access, category and node).</summary>
    public async Task<bool> SeesAsync(Guid id, CancellationToken ct) =>
        await new OpportunityEntities(db, request, visibility).ElementAsync(id, ct).ConfigureAwait(false) is not null;

    /// <summary>Moves an opportunity by hand; a lost stage needs the reason.</summary>
    public async Task MoveAsync(Guid id, Guid stageId, string? lostReason, CancellationToken ct)
    {
        var stages = await StagesAsync(TranslatedText.DefaultLanguage, ct).ConfigureAwait(false);
        var stage = stages.FirstOrDefault(s => s.Id == stageId) ?? throw ValidationException.For("stageId", "There is no such stage.");
        var row = await db.Set<Opportunity>().FirstOrDefaultAsync(o => o.Id == id && o.Active, ct).ConfigureAwait(false) ?? throw ValidationException.For("id", "There is no such opportunity.");
        if (stage.Outcome == Lost && string.IsNullOrWhiteSpace(lostReason))
        {
            throw ValidationException.For("lostReason", "A lost opportunity needs its reason.");
        }

        row.StageId = stageId;
        row.LostReason = stage.Outcome == Lost ? lostReason!.Trim() : null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityForecastDto>> ForecastAsync(string language, CancellationToken ct)
    {
        var board = await BoardAsync(language, ct).ConfigureAwait(false);
        var stages = board.Stages.ToDictionary(s => s.Id);
        return [.. board.Cards.Where(c => c.ExpectedClose is not null && stages[c.StageId!.Value].Outcome != Lost).GroupBy(c => (Month: c.ExpectedClose!.Value.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), c.Currency))
            .Select(g => new OpportunityForecastDto(g.Key.Month, g.Key.Currency, g.Where(c => stages[c.StageId!.Value].Outcome != Won).Sum(c => c.Amount),
                g.Where(c => stages[c.StageId!.Value].Outcome != Won).Sum(c => c.Amount * stages[c.StageId!.Value].Chance / 100), g.Where(c => stages[c.StageId!.Value].Outcome == Won).Sum(c => c.Amount), g.Sum(c => c.Invoiced)))
            .OrderBy(r => r.Month).ThenBy(r => r.Currency)];
    }

    /// <summary>The Opportunities and documents report: each document of the opportunities the user sees, by its day and the opportunity's stage.</summary>
    public async Task<IReadOnlyList<OpportunityDocumentRowDto>> DocumentsReportAsync(DateOnly? from, DateOnly? to, Guid? stage, string language, CancellationToken ct)
    {
        var board = await BoardAsync(language, ct).ConfigureAwait(false);
        var stages = board.Stages.ToDictionary(s => s.Id, s => s.Name);
        var cards = board.Cards.Where(c => stage is null || c.StageId == stage).ToDictionary(c => c.Id);
        return [.. (await DocumentsOfAsync([.. cards.Keys], language, ct).ConfigureAwait(false))
            .Where(d => (from is null || d.Day >= from) && (to is null || d.Day <= to))
            .Select(d => d.Row with { Opportunity = cards[d.OpportunityId].Name, Stage = cards[d.OpportunityId].StageId is { } s ? stages.GetValueOrDefault(s) : null,
                Partner = cards[d.OpportunityId].Partner })
            .OrderBy(r => r.Opportunity).ThenBy(r => r.Date).ThenBy(r => r.Code)];
    }

    /// <summary>
    /// The documents of these opportunities the user sees: those linked to one, and the reversals of those. An invoice is posted and of a type that holds
    /// an open item, or the posted reversal of one (its total then counts against it).
    /// </summary>
    private async Task<List<(Guid OpportunityId, DateOnly Day, bool Reversal, OpportunityDocumentRowDto Row)>> DocumentsOfAsync(List<Guid> opportunities, string language,
        CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var ids = opportunities.Select(i => (Guid?)i).ToList();
        var linked = await db.Set<Document>().AsNoTracking().Where(d => d.Active && ids.Contains(d.OpportunityId) && (d.OwnerId == null || nodes.Contains(d.OwnerId.Value)))
            .ToListAsync(ct).ConfigureAwait(false);
        var linkedIds = linked.Select(d => (Guid?)d.Id).ToList();
        var reversals = await db.Set<Document>().AsNoTracking().Where(d => d.Active && d.Posted && linkedIds.Contains(d.ReversedDocumentId) && d.OpportunityId == null
            && (d.OwnerId == null || nodes.Contains(d.OwnerId.Value))).ToListAsync(ct).ConfigureAwait(false);
        var all = linked.Concat(reversals).ToList();
        var typeIds = all.Select(d => d.TypeId).Distinct().ToList();
        var types = await db.Set<DocumentType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var holding = (await db.Set<DocumentType>().AsNoTracking().Where(t => t.HoldsOpenItem).Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var allIds = all.Select(d => (Guid?)d.Id).ToList();
        var totals = await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.Active && allIds.Contains(t.DocumentId)).GroupBy(t => t.DocumentId!.Value)
            .Select(g => new { g.Key, Total = g.Sum(t => t.ValueVatLcu ?? 0) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct).ConfigureAwait(false);
        var opportunityOf = linked.ToDictionary(d => d.Id, d => d.OpportunityId!.Value);
        return [.. all.Select(d =>
        {
            var type = types[d.TypeId];
            var reversal = d.ReversedDocumentId is not null && type.IsReversal;
            var invoice = d.Posted && (holding.Contains(d.TypeId) || (reversal && type.ReversedDocumentTypeId is { } original && holding.Contains(original)));
            var opportunity = d.OpportunityId ?? opportunityOf[d.ReversedDocumentId!.Value];
            var day = d.PostingDate ?? d.DocumentDate ?? DateOnly.FromDateTime(DateTime.Today);
            return (opportunity, day, reversal, new OpportunityDocumentRowDto(opportunity, null, null, null, d.Id, d.Code, type.Name.In(language), d.DocumentDate ?? d.PostingDate,
                d.Posted, invoice, (reversal ? -1 : 1) * totals.GetValueOrDefault(d.Id)));
        })];
    }

    private async Task<Guid?> BaseCurrencyAsync(CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        return await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.IsBase && c.OwnerId == company).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<OpportunityStageDto>> StagesAsync(string language, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<OpportunityStage>().AsNoTracking().Where(s => s.Active && s.OwnerId != null && nodes.Contains(s.OwnerId.Value)).OrderBy(s => s.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(s => new OpportunityStageDto(s.Id, s.Name.In(language), s.Chance, s.Outcome))];
    }

    private async Task<(Dictionary<Guid, string?> Partners, Dictionary<Guid, string?> Currencies)> NamesAsync(List<Opportunity> rows, string language, CancellationToken ct)
    {
        var partnerIds = rows.Select(o => o.PartnerId).OfType<Guid>().Distinct().ToList();
        var currencyIds = rows.Select(o => o.CurrencyId).OfType<Guid>().Distinct().ToList();
        var partners = await db.Set<Partner>().AsNoTracking().Where(p => partnerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name.In(language), ct).ConfigureAwait(false);
        var currencies = await db.Set<Currency>().AsNoTracking().Where(c => currencyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        return (partners, currencies);
    }
}

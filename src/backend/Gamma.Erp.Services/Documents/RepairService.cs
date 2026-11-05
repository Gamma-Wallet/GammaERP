using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Employees;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

public sealed record RepairStageDto(Guid Id, string? Name, bool IsFinal);

/// <summary>A job on the board. Its item is the first text property of the intake and its date the first date property, so each company's intake decides what they are.</summary>
public sealed record RepairCardDto(Guid Id, Guid IntakeId, string? IntakeCode, Guid? IntakeCategoryId, string? Client, string? Item, string? ComeBackOn, Guid? TechnicianId, string? Technician, Guid RepairStageId,
    Guid? OfferId, string? OfferCode, Guid? OfferCategoryId, string Decision, Guid? InvoiceId, string? InvoiceCode, Guid? InvoiceCategoryId);

public sealed record RepairOptionDto(Guid Id, string? Name);

public sealed record RepairBoardDto(IReadOnlyList<RepairStageDto> Stages, IReadOnlyList<RepairCardDto> Cards, IReadOnlyList<RepairOptionDto> Technicians);

public sealed record RepairDocumentDto(Guid Id, Guid? CategoryId);

/// <summary>A new job: a client found, or a name or a phone to make one from.</summary>
public sealed record NewRepairRequest(Guid? PartnerId, string? Name, string? Phone, Guid? TechnicianId);

/// <summary>
/// Repairs (the founder, 2026-11-02): the client leaves an item and gets an intake document; a normal offer of parts and repair time is made from it; a person marks
/// the offer accepted or refused; the accepted offer makes a draft sales invoice when the client comes back, and parts leave stock when it posts. Everything moves by hand. Which type
/// starts a job, which can be accepted and which flow needs that is configuration.
/// </summary>
public sealed class RepairService(ErpDbContext db, RequestContext request, Visibility visibility, DocumentService documents, TimeProvider clock)
{
    /// <summary>The code of the partner property that holds the phone.</summary>
    private const string PhoneCode = "PHONE";

    private const int FinalDaysShown = 30, ClientsShown = 10;

    public const string Undecided = "open", Accepted = "accepted", Refused = "refused";

    /// <summary>The stages, the jobs not over or over lately, and the technicians.</summary>
    public async Task<RepairBoardDto> BoardAsync(string language, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var stages = await db.Set<RepairStage>().AsNoTracking().Where(s => s.Active && s.OwnerId == company).OrderBy(s => s.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        var finals = stages.Where(s => s.IsFinal).Select(s => s.Id).ToList();
        var since = clock.GetUtcNow().AddDays(-FinalDaysShown);
        var jobs = await db.Set<RepairJob>().AsNoTracking().Where(j => j.Active && j.CompanyId == company && (!finals.Contains(j.RepairStageId) || j.CreatedOn >= since)).OrderBy(j => j.Id).ToListAsync(ct).ConfigureAwait(false);
        var intakeIds = jobs.Select(j => j.IntakeId).ToList();
        var intakes = await db.Set<Document>().AsNoTracking().Where(d => intakeIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct).ConfigureAwait(false);

        // The offer is the latest document of a type that can be accepted made from the intake (the same rule that makes it).
        var links = await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                           join t in db.Set<DocumentType>().AsNoTracking() on d.TypeId equals t.Id
                           where l.Active && d.Active && t.CanBeAccepted && l.Document1Id != null && intakeIds.Contains(l.Document1Id.Value) select new { Intake = l.Document1Id!.Value, Offer = d })
            .ToListAsync(ct).ConfigureAwait(false);
        var offers = links.GroupBy(l => l.Intake).ToDictionary(g => g.Key, g => g.MaxBy(x => x.Offer.Id)!.Offer);
        var offerIds = offers.Values.Select(o => (Guid?)o.Id).ToList();
        var invoices = (await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                               where l.Active && d.Active && offerIds.Contains(l.Document1Id) select new { Offer = l.Document1Id!.Value, Invoice = d }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.Offer).ToDictionary(g => g.Key, g => g.MaxBy(x => x.Invoice.Id)!.Invoice);
        var typeIds = intakes.Values.Concat(offers.Values).Concat(invoices.Values).Select(d => d.TypeId).Distinct().ToList();
        var categories = (await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.Active && typeIds.Contains(l.TypeId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.TypeId).ToDictionary(g => g.Key, g => g.First().CategoryId);

        var values = await (from v in db.Set<DocumentInfo>().AsNoTracking() join t in db.Set<DocumentTypeInfo>().AsNoTracking() on v.TypeInfoId equals t.Id
                            where v.Active && intakeIds.Contains(v.ElementId) orderby t.SortOrder select new { v.ElementId, t.Kind, v.Value }).ToListAsync(ct).ConfigureAwait(false);
        static string? Text(JsonNode? value) => (value is JsonObject texts ? texts.Select(t => t.Value).FirstOrDefault() : value)?.ToString();
        string? First(Guid intake, PropertyKind kind) => values.Where(v => v.ElementId == intake && v.Kind == kind).Select(v => Text(v.Value)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var technicians = await db.Set<Employee>().AsNoTracking().Where(e => e.Active && e.OwnerId != null).ToListAsync(ct).ConfigureAwait(false);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var staff = technicians.Where(e => e.OwnerId == company || nodes.Contains(e.OwnerId!.Value)).Select(e => new RepairOptionDto(e.Id, e.Name.In(language) ?? e.Code)).OrderBy(e => e.Name).ToList();
        var named = technicians.ToDictionary(e => e.Id, e => e.Name.In(language) ?? e.Code);
        Guid? Category(Document? d) => d is not null && categories.TryGetValue(d.TypeId, out var c) ? c : null;

        var cards = jobs.Where(j => intakes.ContainsKey(j.IntakeId)).Select(j =>
        {
            var intake = intakes[j.IntakeId];
            var offer = offers.GetValueOrDefault(j.IntakeId);
            var invoice = offer is null ? null : invoices.GetValueOrDefault(offer.Id);
            var decision = offer?.AcceptedOn is not null ? Accepted : offer?.RefusedOn is not null ? Refused : Undecided;
            return new RepairCardDto(j.Id, j.IntakeId, intake.Code, Category(intake), intake.ToName, First(j.IntakeId, PropertyKind.Text), First(j.IntakeId, PropertyKind.Date), j.TechnicianId,
                j.TechnicianId is { } t ? named.GetValueOrDefault(t) : null, j.RepairStageId, offer?.Id, offer?.Code, Category(offer), decision, invoice?.Id, invoice?.Code, Category(invoice));
        }).ToList();
        return new RepairBoardDto([.. stages.Select(s => new RepairStageDto(s.Id, s.Name.In(language), s.IsFinal))], cards, staff);
    }

    /// <summary>Clients whose name or phone holds the words typed.</summary>
    public async Task<IReadOnlyList<RepairOptionDto>> ClientsAsync(string search, string language, CancellationToken ct)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return [];
        }

        var phones = (await (from v in db.Set<PartnerInfo>().AsNoTracking() join t in db.Set<PartnerTypeInfo>().AsNoTracking() on v.TypeInfoId equals t.Id
                             where v.Active && t.Kind == PropertyKind.Text && t.Code == PhoneCode select new { v.ElementId, v.Value }).ToListAsync(ct).ConfigureAwait(false))
            .Where(v => v.Value?.ToJsonString().Contains(term, StringComparison.OrdinalIgnoreCase) == true)
            .Select(v => v.ElementId).ToList();
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var partners = await db.Set<Partner>().AsNoTracking().Where(p => p.Active).ToListAsync(ct).ConfigureAwait(false);
        return [.. partners.Where(p => (p.OwnerId is null || nodes.Contains(p.OwnerId.Value)) && (phones.Contains(p.Id) || p.Name.In(language)?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
            .Select(p => new RepairOptionDto(p.Id, p.Name.In(language))).OrderBy(p => p.Name).Take(ClientsShown)];
    }

    /// <summary>A new job: its client found or made, its intake document in the first stage. The person fills the intake's properties next.</summary>
    public async Task<RepairDocumentDto> NewAsync(NewRepairRequest job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        await TechnicianAsync(job.TechnicianId, ct).ConfigureAwait(false);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var type = await db.Set<DocumentType>().AsNoTracking().FirstOrDefaultAsync(t => t.Active && t.StartsRepair && t.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: no document type starts a repair.");
        var stage = await db.Set<RepairStage>().AsNoTracking().Where(s => s.Active && s.OwnerId == company).OrderBy(s => s.SortOrder).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("stage", $"{ErrorCodes.NotConfiguredError}: the repair board has no stages.");
        var client = job.PartnerId is { } id ? await db.Set<Partner>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false) ?? throw ValidationException.For("partnerId", "The client does not exist.")
            : await MakeClientAsync(job.Name, job.Phone, ct).ConfigureAwait(false);
        var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.Active && l.TypeId == type.Id).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var day = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var intake = await documents.SaveAsync(new DocumentDto
        {
            TypeId = type.Id, CategoryIds = categories, DocumentDate = day, PostingDate = day, ToId = client.Id, ToName = client.Name.In(TranslatedText.DefaultLanguage),
        }, ct).ConfigureAwait(false);
        db.Add(new RepairJob { CompanyId = company, IntakeId = intake.Id!.Value, RepairStageId = stage.Id, TechnicianId = job.TechnicianId, TenantId = request.TenantId, OwnerId = company });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RepairDocumentDto(intake.Id!.Value, categories.FirstOrDefault());
    }

    /// <summary>Moves a job on the board, by hand.</summary>
    public async Task MoveAsync(Guid id, Guid stageId, CancellationToken ct)
    {
        var job = await JobAsync(id, ct).ConfigureAwait(false);
        if (!await db.Set<RepairStage>().AsNoTracking().AnyAsync(s => s.Id == stageId && s.Active && s.OwnerId == job.CompanyId, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("stageId", "The stage does not exist.");
        }

        job.RepairStageId = stageId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Gives a job its technician.</summary>
    public async Task AssignAsync(Guid id, Guid? technicianId, CancellationToken ct)
    {
        var job = await JobAsync(id, ct).ConfigureAwait(false);
        await TechnicianAsync(technicianId, ct).ConfigureAwait(false);
        job.TechnicianId = technicianId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Refuses a technician who is not an active employee the request sees.</summary>
    private async Task TechnicianAsync(Guid? technicianId, CancellationToken ct)
    {
        if (technicianId is not { } technician)
        {
            return;
        }

        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        if (!await db.Set<Employee>().AsNoTracking().AnyAsync(e => e.Id == technician && e.Active && (e.OwnerId == null || nodes.Contains(e.OwnerId.Value)), ct).ConfigureAwait(false))
        {
            throw ValidationException.For("technicianId", "The technician is not an employee of this company.");
        }
    }

    /// <summary>The offer from the intake: the flow from the type that starts a job to a type that can be accepted. A person writes its parts and repair time next.</summary>
    public async Task<RepairDocumentDto> OfferAsync(Guid id, CancellationToken ct)
    {
        var job = await JobAsync(id, ct).ConfigureAwait(false);
        var intake = await db.Set<Document>().AsNoTracking().FirstAsync(d => d.Id == job.IntakeId, ct).ConfigureAwait(false);
        var target = await (from f in db.Set<DocumentTypeFlow>().AsNoTracking() join t in db.Set<DocumentType>().AsNoTracking() on f.TargetTypeId equals t.Id
                            where f.Active && f.SourceTypeId == intake.TypeId && t.CanBeAccepted select (Guid?)t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: the intake makes no offer that can be accepted.");
        return await MadeAsync(await documents.MakeAsync(job.IntakeId, target, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
    }

    /// <summary>The draft invoice from the accepted offer, when the client comes back: the flow that needs acceptance, checked where every flow is.</summary>
    public async Task<RepairDocumentDto> BillAsync(Guid id, CancellationToken ct)
    {
        var job = await JobAsync(id, ct).ConfigureAwait(false);
        var offer = await OfferOfAsync(job, ct).ConfigureAwait(false);
        var target = await db.Set<DocumentTypeFlow>().AsNoTracking().Where(f => f.Active && f.SourceTypeId == offer.TypeId && f.NeedsAcceptance).Select(f => (Guid?)f.TargetTypeId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: this offer makes no invoice.");
        return await MadeAsync(await documents.MakeAsync(offer.Id, target, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
    }

    /// <summary>The client accepts the offer, or changes their mind: a person says so.</summary>
    public async Task DecideAsync(Guid id, bool accepted, CancellationToken ct)
    {
        var job = await JobAsync(id, ct).ConfigureAwait(false);
        var offer = await OfferOfAsync(job, ct).ConfigureAwait(false);
        if (await (from l in db.Set<InterDocumentDocument>().AsNoTracking() join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                   where l.Active && d.Active && l.Document1Id == offer.Id select d.Id).AnyAsync(ct).ConfigureAwait(false))
        {
            throw ValidationException.For("id", $"{ErrorCodes.NotAllowedError}: the offer has an invoice; the decision can't change.");
        }

        var now = clock.GetUtcNow();
        (offer.AcceptedBy, offer.AcceptedOn) = accepted ? (request.UserId, now) : ((Guid?)null, (DateTimeOffset?)null);
        (offer.RefusedBy, offer.RefusedOn) = accepted ? ((Guid?)null, (DateTimeOffset?)null) : (request.UserId, now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<RepairDocumentDto> MadeAsync(DocumentDto made, CancellationToken ct)
    {
        var category = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.Active && l.TypeId == made.TypeId).Select(l => (Guid?)l.CategoryId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new RepairDocumentDto(made.Id!.Value, category);
    }

    private async Task<Document> OfferOfAsync(RepairJob job, CancellationToken ct)
    {
        var made = db.Set<InterDocumentDocument>().Where(l => l.Active && l.Document1Id == job.IntakeId).Select(l => l.Document2Id);
        var offerTypes = db.Set<DocumentType>().Where(t => t.CanBeAccepted).Select(t => t.Id);
        return await db.Set<Document>().Where(d => d.Active && made.Contains(d.Id) && offerTypes.Contains(d.TypeId)).OrderByDescending(d => d.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "This job has no offer yet.");
    }

    private async Task<RepairJob> JobAsync(Guid id, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        return await db.Set<RepairJob>().FirstOrDefaultAsync(j => j.Id == id && j.Active && companies.Contains(j.CompanyId), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The repair does not exist.");
    }

    // A client from a name or a phone, of the kind and categories of the company's general client; with only a phone, the phone is the name.
    private async Task<Partner> MakeClientAsync(string? name, string? phone, CancellationToken ct)
    {
        var (given, number) = (name?.Trim(), phone?.Trim());
        if (string.IsNullOrEmpty(given) && string.IsNullOrEmpty(number))
        {
            throw ValidationException.For("name", "Give the client's name or phone.");
        }

        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var general = (await db.Set<PointOfSaleSetting>().AsNoTracking().Where(s => s.Active && s.WalkInClientId != null).ToListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(s => s.OwnerId is { } o && nodes.Contains(o))?.WalkInClientId ?? throw ValidationException.For("partnerId", $"{ErrorCodes.NotConfiguredError}: the company has no general client to model a client on.");
        var model = await db.Set<Partner>().AsNoTracking().FirstAsync(p => p.Id == general, ct).ConfigureAwait(false);
        var language = await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false);
        var client = new Partner { TypeId = model.TypeId, Name = TranslatedText.Both(string.IsNullOrEmpty(given) ? number! : given, language), TenantId = request.TenantId, OwnerId = model.OwnerId };
        db.Add(client);
        foreach (var category in await db.Set<InterPartnerCategory>().AsNoTracking().Where(l => l.Active && l.ElementId == model.Id).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false))
        {
            db.Add(new InterPartnerCategory { ElementId = client.Id, CategoryId = category, TenantId = request.TenantId, OwnerId = model.OwnerId });
        }

        if (!string.IsNullOrEmpty(number))
        {
            var property = await db.Set<PartnerTypeInfo>().AsNoTracking().FirstOrDefaultAsync(t => t.Active && t.TypeId == model.TypeId && t.Kind == PropertyKind.Text && t.Code == PhoneCode, ct).ConfigureAwait(false);
            if (property is not null)
            {
                db.Add(new PartnerInfo { ElementId = client.Id, TypeInfoId = property.Id, Value = JsonValue.Create(number), TenantId = request.TenantId, OwnerId = model.OwnerId });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return client;
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Repairs belong to a company: choose one of its nodes.");
}

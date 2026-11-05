using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Seed;

/// <summary>A company to create under the corporate: its name, its country (ISO code) and the cost centres it starts with.</summary>
public sealed record CompanySetup(string Code, string Name, string Country, IReadOnlyList<(string TypeKey, string Code, string Name)>? CostCenters = null,
    string Activity = ActivityCatalogue.General, bool FromCatalogue = false, IReadOnlyCollection<string>? Groups = null);

/// <summary>What <see cref="Organisation.Create"/> made: the corporate, each company by code, and the types the packs build on.</summary>
public sealed record OrganisationIds(Guid TenantId, Guid Corporate, IReadOnlyDictionary<string, Guid> Companies, Guid CorporateType, Guid CompanyType,
    Guid CompanyCategory);

/// <summary>
/// The organisation as the cost centre tree: the corporate is the root, the companies sit under it, their cost centres under them, and the
/// cost centre types form the same tree (Corporate → Company → the types a company's pack brings). A company with a starter pack for its
/// country gets that pack, owned by the company, and the cost centres asked for; the guided start does the same for every new company.
/// </summary>
public static class Organisation
{
    public static OrganisationIds Create(ErpDbContext db, Guid tenantId, string corporateName, string corporateCountry, IReadOnlyList<CompanySetup> companies)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(companies);

        // The corporate's own node, which owns the types above the companies.
        var corporateId = Guid.CreateVersion7();
        var corporateType = new CostCenterType { Id = Guid.CreateVersion7(), Code = "CORPORATE", Name = Text("Corporate", "Korporata"), IsElement = true };
        var companyType = new CostCenterType { Id = Guid.CreateVersion7(), IdSup = corporateType.Id, Code = "COMPANY", Name = Text("Company", "Kompani"), IsElement = true, KeepsBooks = true };
        var companyCategory = new CostCenterCategory { Id = Guid.CreateVersion7(), Code = "COMPANIES", Name = Text("Companies", "Kompani"), IsElement = true };
        var country = new CostCenterTypeInfo { Id = Guid.CreateVersion7(), TypeId = companyType.Id, Name = Text("Country", "Shteti"), Code = "COUNTRY", Kind = PropertyKind.Text, Required = true };
        var activity = new CostCenterTypeInfo { Id = Guid.CreateVersion7(), TypeId = companyType.Id, Name = Text(ActivityProperty, "Aktiviteti"), Kind = PropertyKind.Text, Required = true };
        var (ownership, method, choices) = Ownership(companyType.Id);
        var corporateCountryInfo = new CostCenterTypeInfo { Id = Guid.CreateVersion7(), TypeId = corporateType.Id, Name = Text("Country", "Shteti"), Code = "COUNTRY", Kind = PropertyKind.Text };
        var corporate = new CostCenter { Id = corporateId, TypeId = corporateType.Id, Code = "CORPORATE", Name = TranslatedText.Both(corporateName, StarterPacks.Language(corporateCountry)),
            Language = StarterPacks.Language(corporateCountry) };
        // The organisation's own definitions (what a corporate and a company are) belong to no single node: every node reads them.
        foreach (var row in new Row[] { corporateType, companyType, companyCategory, country, activity, ownership, method, corporateCountryInfo }.Concat(choices))
        {
            Own(db, row, tenantId, null);
        }

        Own(db, corporate, tenantId, corporateId);

        Own(db, new CostCenterInfo { ElementId = corporateId, TypeInfoId = corporateCountryInfo.Id, Value = Country(corporateCountry) }, tenantId, corporateId);

        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var setup in companies)
        {
            ids[setup.Code] = AddCompany(db, tenantId, corporateId, companyType.Id, companyCategory.Id, country.Id, activity.Id, setup);
        }

        return new OrganisationIds(tenantId, corporateId, ids, corporateType.Id, companyType.Id, companyCategory.Id);
    }

    /// <summary>
    /// The organisation of a corporate as it is in the database (for adding a company to it later): its root, the Company type and the
    /// Companies category, and its companies by code. Null when the root keeps its own books (a single company, with no corporate above it).
    /// </summary>
    public static async Task<OrganisationIds?> FindAsync(ErpDbContext db, Guid tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var root = await db.Set<CostCenter>().AsNoTracking().Where(c => c.TenantId == tenantId && c.IdSup == null).ToListAsync(ct).ConfigureAwait(false);
        var types = await db.Set<CostCenterType>().AsNoTracking().Where(t => t.TenantId == tenantId && t.OwnerId == null).ToListAsync(ct).ConfigureAwait(false);
        if (root.Count != 1 || types.SingleOrDefault(t => t.Id == root[0].TypeId) is not { KeepsBooks: false } corporateType
            || types.SingleOrDefault(t => t.IdSup == corporateType.Id && t.KeepsBooks) is not { } companyType)
        {
            return null;
        }

        var category = await db.Set<CostCenterCategory>().AsNoTracking().SingleAsync(c => c.TenantId == tenantId && c.OwnerId == null && c.Code == "COMPANIES", ct).ConfigureAwait(false);
        var companies = await db.Set<CostCenter>().AsNoTracking().Where(c => c.TenantId == tenantId && c.TypeId == companyType.Id).ToDictionaryAsync(c => c.Code!, c => c.Id, ct)
            .ConfigureAwait(false);
        return new OrganisationIds(tenantId, root[0].Id, companies, corporateType.Id, companyType.Id, category.Id);
    }

    /// <summary>Adds a company under the corporate of <paramref name="organisation"/> (saving is the caller's) and returns its id.</summary>
    public static async Task<Guid> AddCompanyAsync(ErpDbContext db, OrganisationIds organisation, CompanySetup setup, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(organisation);
        ArgumentNullException.ThrowIfNull(setup);
        var settings = await db.Set<CostCenterTypeInfo>().AsNoTracking().Where(i => i.TypeId == organisation.CompanyType).ToListAsync(ct).ConfigureAwait(false);
        Guid Setting(string name) => settings.Single(i => i.Name.In(TranslatedText.DefaultLanguage) == name).Id;
        return AddCompany(db, organisation.TenantId, organisation.Corporate, organisation.CompanyType, organisation.CompanyCategory, Setting("Country"),
            Setting(ActivityProperty), setup);
    }

    private static Guid AddCompany(ErpDbContext db, Guid tenantId, Guid corporateId, Guid companyType, Guid companyCategory, Guid country, Guid activity, CompanySetup setup)
    {
        // A company is a cost centre under the corporate, and owns itself: it sees itself and what is below it, never the corporate.
        var company = new CostCenter { Id = Guid.CreateVersion7(), IdSup = corporateId, TypeId = companyType, Code = setup.Code, Name = TranslatedText.Both(setup.Name, StarterPacks.Language(setup.Country)),
            Language = StarterPacks.Language(setup.Country) };
        Own(db, company, tenantId, company.Id);
        Own(db, new CostCenterInfo { ElementId = company.Id, TypeInfoId = country, Value = Country(setup.Country) }, tenantId, company.Id);
        Own(db, new CostCenterInfo { ElementId = company.Id, TypeInfoId = activity, Value = Activity(setup.Activity) }, tenantId, company.Id);
        Own(db, new InterCostCenterCategory { ElementId = company.Id, CategoryId = companyCategory }, tenantId, company.Id);
        return company.Id;
    }

    /// <summary>
    /// A company that is its own root (e.g. a barber shop, a single shop): no corporate above it; its type is a top-level Company type that keeps
    /// its own books, and the pack's types sit under it. The company is both the root and the company.
    /// </summary>
    public static OrganisationIds CreateCompany(ErpDbContext db, Guid tenantId, CompanySetup setup)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(setup);
        var companyType = new CostCenterType { Id = Guid.CreateVersion7(), Code = "COMPANY", Name = Text("Company", "Kompani"), IsElement = true, KeepsBooks = true };
        var companyCategory = new CostCenterCategory { Id = Guid.CreateVersion7(), Code = "COMPANIES", Name = Text("Companies", "Kompani"), IsElement = true };
        var country = new CostCenterTypeInfo { Id = Guid.CreateVersion7(), TypeId = companyType.Id, Name = Text("Country", "Shteti"), Code = "COUNTRY", Kind = PropertyKind.Text, Required = true };
        var activity = new CostCenterTypeInfo { Id = Guid.CreateVersion7(), TypeId = companyType.Id, Name = Text(ActivityProperty, "Aktiviteti"), Kind = PropertyKind.Text, Required = true };
        foreach (var row in new Row[] { companyType, companyCategory, country, activity })
        {
            Own(db, row, tenantId, null);
        }

        var company = new CostCenter { Id = Guid.CreateVersion7(), TypeId = companyType.Id, Code = setup.Code, Name = TranslatedText.Both(setup.Name, StarterPacks.Language(setup.Country)),
            Language = StarterPacks.Language(setup.Country) };
        Own(db, company, tenantId, company.Id);
        Own(db, new CostCenterInfo { ElementId = company.Id, TypeInfoId = country.Id, Value = Country(setup.Country) }, tenantId, company.Id);
        Own(db, new CostCenterInfo { ElementId = company.Id, TypeInfoId = activity.Id, Value = Activity(setup.Activity) }, tenantId, company.Id);
        Own(db, new InterCostCenterCategory { ElementId = company.Id, CategoryId = companyCategory.Id }, tenantId, company.Id);
        return new OrganisationIds(tenantId, company.Id, new Dictionary<string, Guid>(StringComparer.Ordinal) { [setup.Code] = company.Id }, companyType.Id,
            companyType.Id, companyCategory.Id);
    }

    /// <summary>
    /// Gives a company its country's starter pack (when there is one) and the cost centres asked for, of the pack's types (by pack key, e.g.
    /// "cost_center_type:CASH_DESK" for a cash desk). Returns the rows added; saving is the caller's.
    /// </summary>
    public static int SetUpCompany(ErpDbContext db, OrganisationIds organisation, CompanySetup setup)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(organisation);
        ArgumentNullException.ThrowIfNull(setup);
        if (!StarterPacks.Countries.Contains(setup.Country, StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        var companyId = organisation.Companies[setup.Code];
        var (added, keys) = StarterPacks.Add(db, setup.Country.ToUpperInvariant(), organisation.TenantId, companyId,
            new Dictionary<string, Guid> { ["CompanyType"] = organisation.CompanyType, ["CompanyCategory"] = organisation.CompanyCategory }, setup.Groups,
            setup.FromCatalogue ? ActivityCatalogue.ProfileFor(setup.Country, setup.Activity) : null);
        foreach (var (typeKey, code, name) in setup.CostCenters ?? [])
        {
            Own(db, new CostCenter { IdSup = companyId, TypeId = keys[typeKey], Code = code, Name = TranslatedText.Both(name, StarterPacks.Language(setup.Country)) }, organisation.TenantId, companyId);
            added++;
        }

        return added;
    }

    /// <summary>
    /// Gives a group (the corporate) its own books from a country's pack (the founder, 2026-10-31: a group behaves as a company of its own;
    /// what passes between it and its companies goes by documents): its chart, document types, units, VAT and menu, owned by the group.
    /// Returns the rows added; saving is the caller's.
    /// </summary>
    public static int SetUpGroup(ErpDbContext db, OrganisationIds organisation, string country, IReadOnlyCollection<string>? groups = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(organisation);
        ArgumentNullException.ThrowIfNull(country);
        if (!StarterPacks.Countries.Contains(country, StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        // The pack's types sit under what owns them: here the Corporate type, so a warehouse goes directly under the group. And where the pack
        // says "the company" (a journal's place), the group is that here: of that type, and in that category.
        Own(db, new InterCostCenterCategory { ElementId = organisation.Corporate, CategoryId = organisation.CompanyCategory }, organisation.TenantId, organisation.Corporate);
        return 1 + StarterPacks.Add(db, country.ToUpperInvariant(), organisation.TenantId, organisation.Corporate,
            new Dictionary<string, Guid> { ["CompanyType"] = organisation.CorporateType, ["CompanyCategory"] = organisation.CompanyCategory }, groups, null).Count;
    }

    /// <summary>
    /// Makes the group and each of its companies the other's partner (the founder, 2026-10-31: what passes between them goes by documents, a
    /// sale from the group is the company's purchase): at the group a partner for each company, at each company one for the group, of the
    /// "Company of the group" partner type in its client and supplier categories, linked by <see cref="Partner.GroupMemberId"/>. Each is made
    /// once; an owner without partner categories yet (no books) is skipped; one set up before the type existed is given it. Saving is the
    /// caller's. Returns the rows added.
    /// </summary>
    public static async Task<int> LinkGroupPartnersAsync(ErpDbContext db, OrganisationIds organisation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(organisation);
        var tenantId = organisation.TenantId;
        var ids = organisation.Companies.Values.Append(organisation.Corporate).ToList();
        var members = await db.Set<CostCenter>().AsNoTracking().Where(c => c.TenantId == tenantId && ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var linked = (await db.Set<Partner>().AsNoTracking().Where(p => p.TenantId == tenantId && p.GroupMemberId != null)
            .Select(p => new { p.OwnerId, p.GroupMemberId }).ToListAsync(ct).ConfigureAwait(false)).Select(p => (p.OwnerId, p.GroupMemberId)).ToHashSet();
        var pairs = organisation.Companies.Values.Where(c => c != organisation.Corporate)
            .SelectMany(c => new[] { (Owner: organisation.Corporate, Member: c), (Owner: c, Member: organisation.Corporate) });
        var added = 0;
        foreach (var (owner, member) in pairs)
        {
            if (linked.Contains((owner, member)) || !members.TryGetValue(member, out var stands))
            {
                continue;
            }

            var categories = await db.Set<PartnerCategory>().AsNoTracking().Where(c => c.TenantId == tenantId && c.OwnerId == owner && c.Active
                && (c.Code == "CLIENTS" || c.Code == "SUPPLIERS")).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
            if (categories.Count == 0)
            {
                continue;
            }

            var type = await db.Set<PartnerType>().AsNoTracking().Where(t => t.TenantId == tenantId && t.OwnerId == owner && t.Code == GroupCompanyType && t.Active)
                .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (type is null)
            {
                var made = new PartnerType { Code = GroupCompanyType, Name = Text("Company of the group", "Kompani e grupit"), IsElement = true, SortOrder = 50 };
                Own(db, made, tenantId, owner);
                foreach (var category in categories)
                {
                    Own(db, new InterPartnerTypeCategory { TypeId = made.Id, CategoryId = category }, tenantId, owner);
                }

                type = made.Id;
                added += 1 + categories.Count;
            }

            var partner = new Partner { TypeId = type.Value, Code = stands.Code, Name = stands.Name, GroupMemberId = member };
            Own(db, partner, tenantId, owner);
            foreach (var category in categories)
            {
                Own(db, new InterPartnerCategory { ElementId = partner.Id, CategoryId = category }, tenantId, owner);
            }

            linked.Add((owner, member));
            added += 1 + categories.Count;
        }

        return added;
    }

    /// <summary>The pack's partner type of the companies of a group (partner_type:GROUP_COMPANY).</summary>
    public const string GroupCompanyType = "GROUP_COMPANY";

    /// <summary>
    /// Completes the cost centres of the company made by the setup (cash desks, bank accounts, warehouses): each one in no category is put in
    /// the first category its type is in (an element is always in one, or nobody sees it), and gets the proposed value
    /// (<see cref="TypeInfo.DefaultElementId"/>) of each property of its type it has no value for, e.g. the cash account of a new cash desk
    /// from the country's pack. Returns the rows added; saving is the caller's.
    /// </summary>
    public static async Task<int> CompleteCostCentersAsync(ErpDbContext db, Guid companyId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var centres = await db.Set<CostCenter>().IgnoreQueryFilters().AsNoTracking().Where(c => c.OwnerId == companyId && c.Active).ToListAsync(ct).ConfigureAwait(false);
        var ids = centres.Select(c => c.Id).ToList();
        var types = centres.Select(c => c.TypeId).ToHashSet();
        var added = 0;

        var placed = (await db.Set<InterCostCenterCategory>().IgnoreQueryFilters().AsNoTracking().Where(l => ids.Contains(l.ElementId) && l.Active)
            .Select(l => l.ElementId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var firstCategory = (await (from l in db.Set<InterCostCenterTypeCategory>().IgnoreQueryFilters()
                                    join c in db.Set<CostCenterCategory>().IgnoreQueryFilters() on l.CategoryId equals c.Id
                                    where types.Contains(l.TypeId) && l.Active && c.Active
                                    select new { l.TypeId, c.Id, c.SortOrder }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.TypeId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).First().Id);
        foreach (var centre in centres.Where(c => !placed.Contains(c.Id) && firstCategory.ContainsKey(c.TypeId)))
        {
            Own(db, new InterCostCenterCategory { ElementId = centre.Id, CategoryId = firstCategory[centre.TypeId] }, centre.TenantId, companyId);
            added++;
        }

        var properties = await db.Set<CostCenterTypeInfo>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.OwnerId == companyId && p.Active && p.DefaultElementId != null).ToListAsync(ct).ConfigureAwait(false);
        var have = (await db.Set<CostCenterInfo>().IgnoreQueryFilters().AsNoTracking().Where(i => ids.Contains(i.ElementId) && i.Active)
            .Select(i => new { i.ElementId, i.TypeInfoId }).ToListAsync(ct).ConfigureAwait(false)).Select(i => (i.ElementId, i.TypeInfoId)).ToHashSet();
        foreach (var centre in centres)
        {
            foreach (var property in properties.Where(p => p.TypeId == centre.TypeId && !have.Contains((centre.Id, p.Id))))
            {
                Own(db, new CostCenterInfo { ElementId = centre.Id, TypeInfoId = property.Id, Value = JsonValue.Create(property.DefaultElementId!.Value.ToString()) },
                    centre.TenantId, companyId);
                added++;
            }
        }

        return added;
    }

    private static void Own(ErpDbContext db, Row row, Guid tenantId, Guid? ownerId)
    {
        row.Id = row.Id == Guid.Empty ? Guid.CreateVersion7() : row.Id;
        (row.TenantId, row.OwnerId) = (tenantId, ownerId);
        db.Add(row);
    }

    private static TranslatedText Text(string english, string albanian) => new() { Values = { ["en"] = english, ["sq"] = albanian } };

    private static JsonObject Country(string code) => new() { ["en"] = code.ToUpperInvariant() };

    /// <summary>
    /// The ownership settings of a company in a group (the founder's decision, 2026-10-29): the corporate's share and how the company is
    /// consolidated. The group's chart always holds the company's entries at 100 %; consolidation applies the share later.
    /// </summary>
    public const string OwnershipProperty = "Ownership %";

    public const string ConsolidationProperty = "Consolidation method";

    private static (CostCenterTypeInfo Ownership, CostCenterTypeInfo Method, IEnumerable<Row> Choices) Ownership(Guid companyType)
    {
        var ownership = new CostCenterTypeInfo { Id = Guid.CreateVersion7(), TypeId = companyType, Name = Text(OwnershipProperty, "Pronësia %"), Kind = PropertyKind.Number };
        var method = new CostCenterTypeInfo { Id = Guid.CreateVersion7(), TypeId = companyType, Name = Text(ConsolidationProperty, "Metoda e konsolidimit"), Kind = PropertyKind.List };
        Row[] choices =
        [
            new CostCenterTypeInfoChoice { TypeInfoId = method.Id, Name = Text("Full (subsidiary)", "E plotë (filial)") },
            new CostCenterTypeInfoChoice { TypeInfoId = method.Id, Name = Text("Proportional (joint operation)", "Proporcionale (operacion i përbashkët)") },
            new CostCenterTypeInfoChoice { TypeInfoId = method.Id, Name = Text("Equity method (associate)", "Metoda e kapitalit (pjesëmarrje)") },
            new CostCenterTypeInfoChoice { TypeInfoId = method.Id, Name = Text("None (investment)", "Asnjë (investim)") },
        ];
        return (ownership, method, choices);
    }

    /// <summary>The company setting (a property of the Company type, next to Country) holding its activity's code from the catalogue.</summary>
    public const string ActivityProperty = "Activity";

    private static JsonObject Activity(string code) => new() { ["en"] = ActivityCatalogue.PathOf(code)[^1].Code };
}

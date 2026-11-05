using System.Text.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using Charts = Gamma.Erp.Models.Charts;
using CostCenters = Gamma.Erp.Models.CostCenters;
using Employees = Gamma.Erp.Models.Employees;
using Opportunities = Gamma.Erp.Models.Opportunities;
using Partners = Gamma.Erp.Models.Partners;
using Products = Gamma.Erp.Models.Products;
using Units = Gamma.Erp.Models.Units;
using Transactions = Gamma.Erp.Models.Transactions;
using Users = Gamma.Erp.Models.Users;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>A row a find tool returns: the element, its type and categories, and the few fields of its own that matter.</summary>
public sealed record FoundDto(Guid Id, string? Code, string? Name, string? Type, IReadOnlyList<string?> Categories, object? More = null);

/// <summary>A register the register tools write: its key, what one row is called, its REST route (the Set up screen's parameter) and its service.</summary>
internal sealed record Register(string Key, string What, string Route, IEntityRows Rows, bool RowsHere = true, string[]? SetupScreens = null, string[]? SetupParameters = null)
{
    /// <summary>The screen (and parameter) its categories and types are set up on: the register's Set up screen, or the type registers' own.</summary>
    public (string[] Screens, string[] Parameters) Setup => (SetupScreens ?? ["setup"], SetupParameters ?? (SetupScreens is null ? [Route] : []));
}

/// <summary>
/// What the tools share to reach the registers: each entity's service built exactly as the REST routes build it (Endpoints.MapEntity), the menu
/// checks the routes' filters make (RequireScreen / RequireReport / RequireAdministrator), the sizes of a page, and finding a row by its code.
/// </summary>
internal static class Registers
{
    public const string English = TranslatedText.DefaultLanguage;
    public const int DefaultPage = 25;
    public const int MaxPage = 100;

    /// <summary>Whether a given text is this id.</summary>
    public static bool Is(Guid? id, string given) => id is { } known && Guid.TryParse(given, out var parsed) && parsed == known;

    public static (int Page, int Size) Paging(int? page, int? pageSize) => (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultPage, 1, MaxPage));

    public static EntityService<Partners.PartnerCategory, Partners.PartnerType, Partners.InterPartnerTypeCategory, Partners.PartnerTypeInfo, Partners.PartnerTypeInfoChoice,
        Partners.Partner, Partners.PartnerInfo, Partners.InterPartnerCategory, Partners.PartnerAccess> Partners(ErpDbContext db, RequestContext request, Visibility visibility) =>
        new(db, request, visibility);

    public static EntityService<Products.ProductCategory, Products.ProductType, Products.InterProductTypeCategory, Products.ProductTypeInfo, Products.ProductTypeInfoChoice,
        Products.Product, Products.ProductInfo, Products.InterProductCategory, Products.ProductAccess> Products(ErpDbContext db, RequestContext request, Visibility visibility) =>
        new(db, request, visibility);

    public static EntityService<CostCenters.CostCenterCategory, CostCenters.CostCenterType, CostCenters.InterCostCenterTypeCategory, CostCenters.CostCenterTypeInfo,
        CostCenters.CostCenterTypeInfoChoice, CostCenters.CostCenter, CostCenters.CostCenterInfo, CostCenters.InterCostCenterCategory, CostCenters.CostCenterAccess>
        CostCenters(ErpDbContext db, RequestContext request, Visibility visibility) => new(db, request, visibility) { Check = CostCenterRules.CompanyLevel(db) };

    public static EntityService<Charts.ChartCategory, Charts.ChartType, Charts.InterChartTypeCategory, Charts.ChartTypeInfo, Charts.ChartTypeInfoChoice,
        Charts.Chart, Charts.ChartInfo, Charts.InterChartCategory, Charts.ChartAccess> Charts(ErpDbContext db, RequestContext request, Visibility visibility) =>
        new(db, request, visibility);

    public static EntityService<Opportunities.OpportunityCategory, Opportunities.OpportunityType, Opportunities.InterOpportunityTypeCategory, Opportunities.OpportunityTypeInfo,
        Opportunities.OpportunityTypeInfoChoice, Opportunities.Opportunity, Opportunities.OpportunityInfo, Opportunities.InterOpportunityCategory, Opportunities.OpportunityAccess>
        Opportunities(ErpDbContext db, RequestContext request, Visibility visibility) => new(db, request, visibility);

    public static EntityService<Users.UserCategory, Users.UserType, Users.InterUserTypeCategory, Users.UserTypeInfo, Users.UserTypeInfoChoice,
        Users.User, Users.UserInfo, Users.InterUserCategory, Users.UserAccess> Users(ErpDbContext db, RequestContext request, Visibility visibility) =>
        new(db, request, visibility);

    public static EntityService<Units.UnitCategory, Units.UnitType, Units.InterUnitTypeCategory, Units.UnitTypeInfo, Units.UnitTypeInfoChoice,
        Units.Unit, Units.UnitInfo, Units.InterUnitCategory, Units.UnitAccess> Units(ErpDbContext db, RequestContext request, Visibility visibility) =>
        new(db, request, visibility);

    public static EntityService<Employees.EmployeeCategory, Employees.EmployeeType, Employees.InterEmployeeTypeCategory, Employees.EmployeeTypeInfo, Employees.EmployeeTypeInfoChoice,
        Employees.Employee, Employees.EmployeeInfo, Employees.InterEmployeeCategory, Employees.EmployeeAccess> Employees(ErpDbContext db, RequestContext request, Visibility visibility) =>
        new(db, request, visibility);

    public static EntityService<Transactions.TransactionCategory, Transactions.TransactionType, Transactions.InterTransactionTypeCategory, Transactions.TransactionTypeInfo,
        Transactions.TransactionTypeInfoChoice, Transactions.Transaction, Transactions.TransactionInfo, Transactions.InterTransactionCategory, Transactions.TransactionAccess>
        Transactions(ErpDbContext db, RequestContext request, Visibility visibility) => new(db, request, visibility);

    /// <summary>The register a tool names: partners, products, accounts, units or cost centres (a few other words for each are taken too).</summary>
    public static Register Of(string given, ErpDbContext db, RequestContext request, Visibility visibility, Gamma.Erp.Services.Documents.DocumentService? documents = null)
    {
        var key = (given ?? "").Trim().ToUpperInvariant().Replace(" ", "-", StringComparison.Ordinal).Replace("_", "-", StringComparison.Ordinal);
        return key switch
        {
            "PARTNERS" or "PARTNER" or "CLIENTS" or "CLIENT" or "SUPPLIERS" or "SUPPLIER" or "CLIENTS-AND-SUPPLIERS" =>
                new("partners", "partner", "partners", Partners(db, request, visibility)),
            "PRODUCTS" or "PRODUCT" or "SERVICES" or "SERVICE" or "PRODUCTS-AND-SERVICES" => new("products", "product", "products", Products(db, request, visibility)),
            "ACCOUNTS" or "ACCOUNT" or "CHART" or "CHARTS" or "CHART-OF-ACCOUNTS" => new("accounts", "account", "charts", Charts(db, request, visibility)),
            "UNITS" or "UNIT" => new("units", "unit", "units", Units(db, request, visibility)),
            "COST-CENTRES" or "COST-CENTRE" or "COST-CENTERS" or "COST-CENTER" or "PLACES" or "PLACE" =>
                new("cost-centres", "cost centre", "cost-centers", CostCenters(db, request, visibility)),
            "EMPLOYEES" or "EMPLOYEE" or "STAFF" => new("employees", "employee", "employees", Employees(db, request, visibility)),
            "OPPORTUNITIES" or "OPPORTUNITY" => new("opportunities", "opportunity", "opportunities", Opportunities(db, request, visibility)),
            "USERS" or "USER" or "ROLES" or "ROLE" => new("users", "user", "users", Users(db, request, visibility), RowsHere: false),
            "TRANSACTIONS" or "TRANSACTION" or "LINE-TYPES" or "LINES" =>
                new("transactions", "line type", "transactions", Transactions(db, request, visibility), RowsHere: false, SetupScreens: ["entities"], SetupParameters: ["transactions"]),
            "DOCUMENTS" or "DOCUMENT" or "DOCUMENT-TYPES" => documents is null ? throw new McpException("Document types are not reachable here.")
                : new("documents", "document", "documents", documents.Entities, RowsHere: false, SetupScreens: ["document-types"]),
            _ => throw new McpException($"There is no register \"{given}\". Registers: partners, products, accounts, units, cost-centres, employees, opportunities; for set-up only, documents, transactions (line types) and users (roles are its categories)."),
        };
    }

    /// <summary>As RequireScreen / RequireScreenWith: one of these screens (with one of these parameters, when named) must be in the user's menu.</summary>
    public static async Task Screen(Visibility visibility, string[] screens, string[] parameters, CancellationToken ct)
    {
        if (!await visibility.OpensScreenAsync(screens, parameters, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: this screen is not in your menu.");
        }
    }

    /// <summary>As RequireScreen("key").</summary>
    public static Task Screen(Visibility visibility, string screen, CancellationToken ct) => Screen(visibility, [screen], [], ct);

    /// <summary>As RequireReport(...): the report screen with one of these reports.</summary>
    public static Task Report(Visibility visibility, CancellationToken ct, params string[] reports) => Screen(visibility, ["report"], reports, ct);

    /// <summary>As RequireAdministrator.</summary>
    public static void Administrator(RequestContext request)
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator does this.");
        }
    }

    public static string? Name(Dictionary<string, string>? name) =>
        name is null ? null : name.TryGetValue(English, out var english) && !string.IsNullOrWhiteSpace(english) ? english : name.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>A category of a register by its id, code or name (case ignored); null when none is given.</summary>
    public static Guid? Category(IReadOnlyList<CategoryDto> categories, string? given, string what)
    {
        if (string.IsNullOrWhiteSpace(given))
        {
            return null;
        }

        var key = given.Trim();
        var found = categories.Where(c => c.Id is not null && (Registers.Is(c.Id, key) || string.Equals(c.Code, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Name(c.Name), key, StringComparison.OrdinalIgnoreCase))).Select(c => c.Id!.Value).Distinct().ToList();
        return found.Count switch
        {
            1 => found[0],
            0 => throw new McpException($"There is no {what} category \"{key}\" you may see. Known: {string.Join(", ", categories.Where(c => c.Active).Select(c => c.Code ?? Name(c.Name)))}."),
            _ => throw new McpException($"Several {what} categories match \"{key}\": give its id."),
        };
    }

    /// <summary>A type of a register by its id, code or name.</summary>
    public static ElementTypeDto Type(IReadOnlyList<ElementTypeDto> types, string given, string what)
    {
        var key = given.Trim();
        var found = types.Where(t => t.Active && (Registers.Is(t.Id, key) || string.Equals(t.Code, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Name(t.Name), key, StringComparison.OrdinalIgnoreCase))).ToList();
        return found.Count switch
        {
            1 => found[0],
            0 => throw new McpException($"There is no {what} type \"{key}\". Known: {string.Join(", ", types.Where(t => t.Active && t.IsElement).Select(t => t.Code ?? Name(t.Name)))}."),
            _ => throw new McpException($"Several {what} types match \"{key}\": give its id."),
        };
    }

    /// <summary>
    /// An element by its id or exact code, among those the request may see (the register's own lookup, with its rights): the id is taken as it is,
    /// a code must name exactly one.
    /// </summary>
    public static async Task<Guid> ElementAsync(Func<string, Task<PageDto<LookupOptionDto>>> lookup, string given, string what)
    {
        var key = given.Trim();
        if (Guid.TryParse(key, out var id))
        {
            return id;
        }

        var page = await lookup(key).ConfigureAwait(false);
        var exact = page.Items.Where(o => string.Equals(o.Code, key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 0)
        {
            exact = [.. page.Items.Where(o => string.Equals(o.Name, key, StringComparison.OrdinalIgnoreCase))];
        }

        return exact.Count switch
        {
            1 => exact[0].Id,
            0 => throw new McpException($"No {what} with the code \"{key}\" that you may see: find it first."),
            _ => throw new McpException($"Several {what}s have the code \"{key}\": give the id."),
        };
    }

    /// <summary>
    /// One page of a register as its lookups list it (the request's rights and nodes), each row with its type and categories read after: the rows
    /// were already allowed by the lookup.
    /// </summary>
    public static async Task<PageDto<FoundDto>> FindAsync<TCategory, TType, TTypeCategory, TTypeInfo, TChoice, TElement, TInfo, TElementCategory, TAccess>(
        EntityService<TCategory, TType, TTypeCategory, TTypeInfo, TChoice, TElement, TInfo, TElementCategory, TAccess> service, ErpDbContext db, string? search, string? category,
        string what, int? page, int? pageSize, CancellationToken ct, Func<IReadOnlyList<Guid>, Task<Dictionary<Guid, object>>>? more = null)
        where TCategory : Category, new()
        where TType : ElementType, new()
        where TTypeCategory : InterTypeCategory, new()
        where TTypeInfo : TypeInfo, new()
        where TChoice : TypeInfoChoice, new()
        where TElement : Element, new()
        where TInfo : ElementInfo, new()
        where TElementCategory : InterElementCategory, new()
        where TAccess : ElementAccess, new()
    {
        var (p, size) = Paging(page, pageSize);
        var categories = await service.CategoriesAsync(ct).ConfigureAwait(false);
        var categoryId = Category(categories, category, what);
        var found = await service.LookupAsync(categoryId, null, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), English, p, size, ct).ConfigureAwait(false);
        var ids = found.Items.Select(o => o.Id).ToList();
        var rows = await db.Set<TElement>().AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.TypeId }).ToListAsync(ct).ConfigureAwait(false);
        var typeIds = rows.Select(r => r.TypeId).Distinct().ToList();
        var types = (await db.Set<TType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false)).ToDictionary(t => t.Id, t => t.Name.In(English));
        var links = await db.Set<TElementCategory>().AsNoTracking().Where(l => ids.Contains(l.ElementId)).Select(l => new { l.ElementId, l.CategoryId }).ToListAsync(ct).ConfigureAwait(false);
        var names = categories.Where(c => c.Id is not null).ToDictionary(c => c.Id!.Value, c => Name(c.Name));
        var extra = more is null ? [] : await more(ids).ConfigureAwait(false);
        var typeOf = rows.ToDictionary(r => r.Id, r => r.TypeId);
        return new PageDto<FoundDto>([.. found.Items.Select(o => new FoundDto(o.Id, o.Code, o.Name,
            typeOf.TryGetValue(o.Id, out var t) ? types.GetValueOrDefault(t) : null,
            [.. links.Where(l => l.ElementId == o.Id).Select(l => names.GetValueOrDefault(l.CategoryId)).Where(n => n is not null).Distinct()],
            extra.GetValueOrDefault(o.Id)))], found.Total, found.Page, found.PageSize);
    }

    /// <summary>
    /// Property values given by the property's code (or id or name), over the values the row already has: a text may be given as plain text (it is
    /// the English one), a list choice by its name. The register checks them on saving, as for the screens.
    /// </summary>
    public static Dictionary<Guid, JsonNode?> Values(ElementTypeDto type, IReadOnlyDictionary<string, JsonElement>? given, IReadOnlyDictionary<Guid, JsonNode?>? existing = null)
    {
        var values = existing is null ? new Dictionary<Guid, JsonNode?>() : new Dictionary<Guid, JsonNode?>(existing);
        if (given is null)
        {
            return values;
        }

        var unknown = new List<string>();
        foreach (var (key, value) in given)
        {
            var property = type.Properties.FirstOrDefault(p => p.Active && (string.Equals(p.Code, key, StringComparison.OrdinalIgnoreCase) || Registers.Is(p.Id, key)))
                ?? type.Properties.FirstOrDefault(p => p.Active && string.Equals(Name(p.Name), key, StringComparison.OrdinalIgnoreCase));
            if (property?.Id is not { } id)
            {
                unknown.Add(key);
                continue;
            }

            JsonNode? node = value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : JsonNode.Parse(value.GetRawText());
            if (property.Kind == PropertyKind.Text && value.ValueKind == JsonValueKind.String)
            {
                node = new JsonObject { [English] = value.GetString() };
            }
            else if (property.Kind == PropertyKind.List && value.ValueKind == JsonValueKind.String && !Guid.TryParse(value.GetString(), out _)
                && property.Choices.FirstOrDefault(c => string.Equals(Name(c.Name), value.GetString(), StringComparison.OrdinalIgnoreCase))?.Id is { } choice)
            {
                node = JsonValue.Create(choice.ToString());
            }
            else if (property.Kind == PropertyKind.MultiList && value.ValueKind == JsonValueKind.Array)
            {
                // Several choices, each by its id or its name.
                var picked = new JsonArray();
                foreach (var item in value.EnumerateArray())
                {
                    var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                    var choiceId = Guid.TryParse(text, out var parsed) ? parsed
                        : property.Choices.FirstOrDefault(c => c.Active && string.Equals(Name(c.Name), text, StringComparison.OrdinalIgnoreCase))?.Id
                          ?? throw new McpException($"{property.Code ?? Name(property.Name)} has no choice \"{text}\". Its choices: "
                              + string.Join(", ", property.Choices.Where(c => c.Active).Select(c => Name(c.Name))) + ".");
                    picked.Add(choiceId.ToString());
                }

                node = picked;
            }

            values[id] = node;
        }

        if (unknown.Count > 0)
        {
            throw new McpException($"The type {Name(type.Name)} has no property {string.Join(", ", unknown)}. Its properties: "
                + string.Join(", ", type.Properties.Where(p => p.Active).Select(p => $"{p.Code ?? Name(p.Name)}{(p.Required ? " (required)" : "")}")) + ".");
        }

        return values;
    }

    /// <summary>A type's properties as Claude reads them: code, name, kind, whether required, the choices of a list.</summary>
    public static IEnumerable<object> Properties(ElementTypeDto type) => type.Properties.Where(p => p.Active).OrderBy(p => p.SortOrder).Select(p => new
    {
        p.Id, p.Code, name = Name(p.Name), kind = p.Kind.ToString(), p.Required,
        choices = p.Choices.Count == 0 ? null : p.Choices.Where(c => c.Active).Select(c => new { c.Id, name = Name(c.Name) }),
    });

    /// <summary>
    /// The name an id has in whichever register holds it (a partner, a cost centre, an employee, a product, an account), as the screens show it
    /// next to the id on a document's sides and lines; null when none holds it.
    /// </summary>
    public static async Task<(string? Name, Guid? TypeId)> NameOfAsync(ErpDbContext db, Guid id, CancellationToken ct)
    {
        async Task<(string?, Guid?)?> In<T>() where T : Element
        {
            var row = await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
            return row is null ? null : (row.Name.In(English), row.TypeId);
        }

        return await In<Partners.Partner>().ConfigureAwait(false) ?? await In<CostCenters.CostCenter>().ConfigureAwait(false)
            ?? await In<Products.Product>().ConfigureAwait(false) ?? await In<Charts.Chart>().ConfigureAwait(false)
            ?? await In<Employees.Employee>().ConfigureAwait(false) ?? (null, null);
    }
}

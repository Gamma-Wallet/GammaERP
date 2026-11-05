using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Configuration;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Platform;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Users = Gamma.Erp.Models.Users;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>
/// Setting a company up through Claude: the same services as the setup, register, rights, configuration and skins screens. Each service refuses
/// what the user may not do (most of this is for administrators only), exactly as for the screens.
/// </summary>
[McpServerToolType]
public sealed class SetupTools(ErpDbContext db, RequestContext request, Visibility visibility, ConfigurationService configuration, SkinService skins, SetupService setup,
    AccessService access)
{
    // ---------- Configuration ----------

    [McpServerTool(Name = "read_configuration", Title = "Read configuration rows", ReadOnly = true)]
    [Description("Reads a configuration model's rows (the rules the engine reads: payment methods and their cash desks, point-of-sale settings, VAT groups, scenarios, document type flows, sale prices…), optionally filtered by column = value. Without a model: the list of models. withSchema adds the model's columns, as needed to write a row. For administrators and users with that configuration screen in their menu.")]
    public Task<object> ReadConfigurationAsync(
        [Description("The model's key, e.g. payment-methods, payment-method-cost-centers, point-of-sale-settings. Empty: the list of models.")] string? model = null,
        [Description("Optional: column → value; only rows whose column equals the value.")] Dictionary<string, string>? filters = null,
        [Description("Also return the model's columns (name, kind, required, what a link points at).")] bool withSchema = false,
        [Description("Page number, from 1.")] int? page = null, [Description("Rows per page: 25 if not given, at most 100.")] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            if (string.IsNullOrWhiteSpace(model))
            {
                return ConfigurationService.Models.Keys.Order(StringComparer.Ordinal).ToList();
            }

            var key = model.Trim();
            await Registers.Screen(visibility, ["config"], [key], ct).ConfigureAwait(false);
            var rows = await configuration.ListAsync(key, filters ?? [], ct).ConfigureAwait(false);
            var (p, size) = Registers.Paging(page, pageSize);
            return new { model = key, count = rows.Count, page = p, pageSize = size, rows = rows.Skip((p - 1) * size).Take(size), schema = withSchema ? configuration.Schema(key) : null };
        });

    [McpServerTool(Name = "save_configuration_row", Title = "Save a configuration row", ReadOnly = false, Destructive = false)]
    [Description("Adds a row to a configuration model (no id) or changes one (with its id; the columns given change, the old row is kept as history). Covers e.g. payment methods by cost centre and point-of-sale settings. Administrators only. Read the model with read_configuration (withSchema) first. Show the user the row as it will be saved and get an explicit yes; then confirm=true. Returns the saved row.")]
    public Task<object> SaveConfigurationRowAsync(
        [Description("True only after the user said yes to this row.")] bool confirm,
        [Description("The model's key, as read_configuration lists it.")] string model,
        [Description("The row as JSON: column → value (links by id); \"id\" to change an existing row.")] JsonElement row, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the configuration row as it will be saved");
            Erp.NeedsPlace(request);
            if (row.ValueKind != JsonValueKind.Object || JsonNode.Parse(row.GetRawText()) is not JsonObject input)
            {
                throw new McpException("The row is a JSON object: column → value.");
            }

            return await configuration.SaveAsync(model.Trim(), input, ct).ConfigureAwait(false);
        });

    // ---------- Skins ----------

    [McpServerTool(Name = "list_skins", Title = "Skins", ReadOnly = true)]
    [Description("The company's skins (looks of the screens), which one is in use for each kind, dark or light. Administrators only.")]
    public Task<object> ListSkinsAsync(CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            return (await skins.ListAsync(ct).ConfigureAwait(false)).Select(s => new { s.Id, s.Name, s.Kind, s.Dark, s.InUse });
        });

    [McpServerTool(Name = "use_skin", Title = "Put a skin in use", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Puts a skin in use for its kind; the one in use before no longer is. Everyone of the company sees it. Administrators only. Tell the user which skin will be used and get an explicit yes; then confirm=true.")]
    public Task<object> UseSkinAsync([Description("True only after the user said yes.")] bool confirm, [Description("The skin: id or name, from list_skins.")] string skin,
        CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "which skin will be put in use");
            Erp.NeedsPlace(request);
            var key = skin.Trim();
            var all = await skins.ListAsync(ct).ConfigureAwait(false);
            var found = all.Where(s => Registers.Is(s.Id, key) || string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count != 1)
            {
                throw new McpException(found.Count == 0 ? $"There is no skin \"{key}\": {string.Join(", ", all.Select(s => s.Name))}." : $"Several skins are called \"{key}\": give the id.");
            }

            var used = await skins.UseAsync(found[0].Id, ct).ConfigureAwait(false);
            return new { used.Id, used.Name, used.Kind, used.InUse };
        });

    // ---------- Companies and places ----------

    [McpServerTool(Name = "add_company_to_group", Title = "Add a company to the group", ReadOnly = false, Destructive = false)]
    [Description("Adds a company to the group, set up from its country's starter pack (for its activity profile, only the pack groups named) or copied from another company of the group, with its first cash desks and bank accounts. Only an administrator working at the group (corporate). Show the user what will be made and get an explicit yes; then confirm=true. Returns the new company's id and how many configuration rows it got.")]
    public Task<object> AddCompanyToGroupAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The company's name.")] string name,
        [Description("Its country (ISO code, e.g. AL, IT, CY).")] string country,
        [Description("Optional: its short code; made from the name when left out.")] string? code = null,
        [Description("Optional: the activity profile (e.g. general, retail, restaurant); general when left out.")] string? activity = null,
        [Description("Optional: only these pack groups; all when left out.")] IReadOnlyList<string>? groups = null,
        [Description("Optional: the id of a company of the group to copy the configuration from, instead of the pack.")] Guid? copyFrom = null,
        [Description("Optional: names of the cash desks to make.")] IReadOnlyList<string>? cashDesks = null,
        [Description("Optional: names of the bank accounts to make.")] IReadOnlyList<string>? banks = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the company to add (name, country, activity, where its configuration comes from)");
            Erp.NeedsPlace(request);
            var made = await setup.AddCompanyAsync(new CompanyRequest(name, country, code, string.IsNullOrWhiteSpace(activity) ? Seed.ActivityCatalogue.General : activity.Trim(),
                Groups: groups, CopyFrom: copyFrom, CashDesks: cashDesks, Banks: banks), request, ct).ConfigureAwait(false);
            return new { company = made.Company, configurationRows = made.Rows };
        });

    [McpServerTool(Name = "set_company_details", Title = "Set the company's details", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Sets the details of the company the user works in, by the codes of its properties (e.g. TRADE_NAME, TAX_NUMBER, ADDRESS, CITY, PHONE, EMAIL — the company type's own properties; an unknown code is refused with the list). Only the given ones change. Show the user the new values and get an explicit yes; then confirm=true. Returns the company's details.")]
    public Task<object> SetCompanyDetailsAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("Property code → value; an empty value clears it.")] Dictionary<string, JsonElement> details, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the company's new details");
            Erp.NeedsPlace(request);
            var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw new McpException("Choose a workplace in a company first (choose_workplace).");
            var places = Registers.CostCenters(db, request, visibility);
            var element = await places.ElementAsync(company, ct).ConfigureAwait(false) ?? throw new McpException("You may not see the company's details.");
            var type = (await places.TypesAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => t.Id == element.TypeId) ?? throw new McpException("The company's type is not found.");
            var saved = await places.SaveElementAsync(element with { Values = Registers.Values(type, details, element.Values) }, ct).ConfigureAwait(false);
            return Shown(saved, type);
        });

    [McpServerTool(Name = "add_cost_center", Title = "Add a cost centre", ReadOnly = false, Destructive = false)]
    [Description("Adds a cost centre: a warehouse, cash desk, bank account, point of sale, or a place under one (a till, a table…), of a cost-centre type, under a parent, with its property values (the type's required ones must be given; an unknown or missing one is refused with the list). Show the user what will be made and get an explicit yes; then confirm=true. Returns the new cost centre.")]
    public Task<object> AddCostCenterAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The cost-centre type: id, code (e.g. WAREHOUSE, CASH_DESK, BANK) or name.")] string type,
        [Description("The parent cost centre (the company, or the point of sale a till is under): id or exact code.")] string parent,
        [Description("Its name.")] string name,
        [Description("Optional: its code; the type's next code when left out.")] string? code = null,
        [Description("Optional: the category (code, name or id) when the type is in several.")] string? category = null,
        [Description("Its property values: property code → value.")] Dictionary<string, JsonElement>? properties = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the cost centre to add (type, parent, name, values)");
            Erp.NeedsPlace(request);
            var places = Registers.CostCenters(db, request, visibility);
            var typeDto = Registers.Type(await places.TypesAsync(ct).ConfigureAwait(false), type, "cost centre");
            var parentId = await Registers.ElementAsync(s => places.LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), parent, "cost centre").ConfigureAwait(false);
            var categoryId = Registers.Category(await places.CategoriesAsync(ct).ConfigureAwait(false), category, "cost centre")
                ?? (typeDto.CategoryIds.Count == 1 ? typeDto.CategoryIds[0] : throw new McpException("The type is in several categories (or none): give the category."));
            var saved = await places.SaveElementAsync(new ElementDto
            {
                TypeId = typeDto.Id!.Value, IdSup = parentId, Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim(), Name = new() { [Registers.English] = name.Trim() },
                CategoryIds = [categoryId], Values = Registers.Values(typeDto, properties),
            }, ct).ConfigureAwait(false);
            return Shown(saved, typeDto);
        });

    // ---------- Users ----------

    [McpServerTool(Name = "add_user", Title = "Add a user", ReadOnly = false, Destructive = false)]
    [Description("Adds a user as the Users register does: name, sign-in name, e-mail (for forgotten-password codes), the places they work at, their roles, and a first password given by the administrator, which the user must change at the first sign-in. All or nothing. Administrators only. Show the user what will be made (never repeat the password back) and get an explicit yes; then confirm=true.")]
    public Task<object> AddUserAsync(
        [Description("True only after the administrator said yes.")] bool confirm,
        [Description("The sign-in name: 3 to 64 letters, digits, dots, dashes or underscores.")] string username,
        [Description("The person's name as shown.")] string name,
        [Description("The first password (the user changes it at the first sign-in).")] string password,
        [Description("The roles (user categories): codes, names or ids.")] IReadOnlyList<string> roles,
        [Description("The places they work at (cost centres): ids or exact codes.")] IReadOnlyList<string> places,
        [Description("Optional: the e-mail address.")] string? email = null,
        [Description("Optional: the user type (id, code or name) when there are several.")] string? type = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the user to add (name, sign-in name, e-mail, places, roles)");
            Erp.NeedsPlace(request);
            Administrator();
            var users = Registers.Users(db, request, visibility);
            var types = (await users.TypesAsync(ct).ConfigureAwait(false)).Where(t => t.Active && t.IsElement).ToList();
            var typeDto = !string.IsNullOrWhiteSpace(type) ? Registers.Type(types, type, "user")
                : types.Count == 1 ? types[0] : throw new McpException($"Give the user type: {string.Join(", ", types.Select(t => t.Code ?? Registers.Name(t.Name)))}.");
            var roleIds = await RolesAsync(users, roles, ct).ConfigureAwait(false);
            var placeIds = await PlacesAsync(places, ct).ConfigureAwait(false);

            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var user = await users.SaveElementAsync(new ElementDto { TypeId = typeDto.Id!.Value, Name = new() { [Registers.English] = name.Trim() }, CategoryIds = roleIds }, ct)
                .ConfigureAwait(false);
            var id = user.Id!.Value;
            await access.SetUsernameAsync(id, new UsernameChange(username), ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(email))
            {
                await access.SetEmailAsync(id, new UserEmail(email), ct).ConfigureAwait(false);
            }

            await access.SetCostCentersAsync(id, placeIds, ct).ConfigureAwait(false);
            await access.ResetPasswordAsync(id, new PasswordReset(password), ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return await UserAsync(id, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "set_user_access", Title = "Set a user's places and roles", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Sets where an existing user works and their roles: each list given replaces the user's current one (left out: kept). All or nothing. Administrators only. Show the administrator the new places and roles and get an explicit yes; then confirm=true.")]
    public Task<object> SetUserAccessAsync(
        [Description("True only after the administrator said yes.")] bool confirm,
        [Description("The user: id or sign-in name.")] string user,
        [Description("Optional: the places they work at (ids or exact codes); replaces the current ones.")] IReadOnlyList<string>? places = null,
        [Description("Optional: their roles (codes, names or ids); replaces the current ones.")] IReadOnlyList<string>? roles = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the user's new places and roles");
            Erp.NeedsPlace(request);
            Administrator();
            var key = user.Trim();
            var id = Guid.TryParse(key, out var given) ? given
                : await db.Set<Users.User>().AsNoTracking().Where(u => u.Username == SignInService.Normalize(key)).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw new McpException($"There is no user \"{key}\".");
            var users = Registers.Users(db, request, visibility);
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (roles is not null)
            {
                var element = await users.ElementAsync(id, ct).ConfigureAwait(false) ?? throw new McpException("There is no such user that you may see.");
                await users.SaveElementAsync(element with { CategoryIds = await RolesAsync(users, roles, ct).ConfigureAwait(false) }, ct).ConfigureAwait(false);
            }

            if (places is not null)
            {
                await access.SetCostCentersAsync(id, await PlacesAsync(places, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return await UserAsync(id, ct).ConfigureAwait(false);
        });

    // ---------- Helpers ----------

    // As AccessService: rights and where users work are changed by administrators only (checked first, so nothing is half made).
    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("access", $"{ErrorCodes.NotAllowedError}: only an administrator changes rights and where users work.");
        }
    }

    private static async Task<List<Guid>> RolesAsync(Services.Entities.EntityService<Users.UserCategory, Users.UserType, Users.InterUserTypeCategory, Users.UserTypeInfo,
        Users.UserTypeInfoChoice, Users.User, Users.UserInfo, Users.InterUserCategory, Users.UserAccess> users, IReadOnlyList<string> roles, CancellationToken ct)
    {
        var categories = await users.CategoriesAsync(ct).ConfigureAwait(false);
        var ids = roles.Select(r => Registers.Category(categories, r, "role")!.Value).Distinct().ToList();
        return ids.Count > 0 ? ids : throw new McpException("A user has at least one role.");
    }

    private async Task<List<Guid>> PlacesAsync(IReadOnlyList<string> places, CancellationToken ct)
    {
        var lookup = Registers.CostCenters(db, request, visibility);
        var ids = new List<Guid>();
        foreach (var place in places)
        {
            ids.Add(await Registers.ElementAsync(s => lookup.LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), place, "cost centre").ConfigureAwait(false));
        }

        return [.. ids.Distinct()];
    }

    private async Task<object> UserAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Set<Users.User>().AsNoTracking().FirstAsync(u => u.Id == id, ct).ConfigureAwait(false);
        var roleIds = await db.Set<Users.InterUserCategory>().AsNoTracking().Where(l => l.ElementId == id).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var roles = await db.Set<Users.UserCategory>().AsNoTracking().Where(c => roleIds.Contains(c.Id)).ToListAsync(ct).ConfigureAwait(false);
        var placeIds = await access.CostCentersAsync(id, ct).ConfigureAwait(false);
        var places = await db.Set<Models.CostCenters.CostCenter>().AsNoTracking().Where(c => placeIds.Contains(c.Id)).ToListAsync(ct).ConfigureAwait(false);
        return new
        {
            row.Id, row.Username, name = row.Name.In(Registers.English), row.Email, mustChangePassword = row.MustChangePassword,
            roles = roles.Select(r => new { r.Id, r.Code, name = r.Name.In(Registers.English) }),
            places = places.Select(p => new { p.Id, p.Code, name = p.Name.In(Registers.English) }),
        };
    }

    private static object Shown(ElementDto element, ElementTypeDto type) => new
    {
        element.Id, element.Code, name = Registers.Name(element.Name), type = Registers.Name(type.Name), parentId = element.IdSup,
        values = type.Properties.Where(p => p.Active && p.Id is not null && element.Values.ContainsKey(p.Id.Value))
            .ToDictionary(p => p.Code ?? Registers.Name(p.Name) ?? p.Id.ToString()!, p => element.Values[p.Id!.Value]),
    };
}

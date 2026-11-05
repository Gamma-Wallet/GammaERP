using System.ComponentModel;
using System.Text.Json;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Audit;
using Gamma.Erp.Services.Configuration;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Platform;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>
/// The administrator's and the signed-in user's own screens through Claude: sign-in names, e-mails and passwords, personal tokens, the user
/// activity (audit), skins, a group's books, the statutory chart, the starter-pack lists, document types' settings and a document's details.
/// Each checks what its route checks (administrator, screen); the services refuse the rest.
/// </summary>
[McpServerToolType]
public sealed class AdminTools(ErpDbContext db, RequestContext request, Visibility visibility, AccessService access, TokenService tokens, AuditService audit,
    SkinService skins, SetupService setup, StatutoryChartService statutory, ConfigurationService configuration, DocumentService documents,
    Gamma.Erp.Services.Printing.PrintService print)
{
    // ---------- Users: sign-in name, e-mail, password ----------

    [McpServerTool(Name = "user_sign_in", Title = "A user's sign-in name, e-mail, password and places", ReadOnly = false, Destructive = false)]
    [Description("A user's sign-in, as the Users register's Sign-in tab does: action read (sign-in name, e-mail, places), username (a new sign-in name: 3 to 64 letters, digits, dots, dashes or underscores, unique), email (a new e-mail for forgotten-password codes; empty clears it), password (a new first password the user must change at the next sign-in; never repeat it back), or places (the places the user works at: the whole list). Administrators only. Changes need the administrator's explicit yes; then confirm=true.")]
    public Task<object> UserSignInAsync(
        [Description("read, username, email, password or places.")] string action,
        [Description("The user: id or exact code (or name).")] string user,
        [Description("username, email, password: the new value.")] string? value = null,
        [Description("places: the cost centres (ids or exact codes), the whole list.")] IReadOnlyList<string>? places = null,
        [Description("True only after the administrator said yes (all but read).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            Registers.Administrator(request);
            var id = await Registers.ElementAsync(s => Registers.Users(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), user, "user")
                .ConfigureAwait(false);
            var what = action.Trim().ToUpperInvariant();
            if (what != "READ")
            {
                Erp.NeedsYes(confirm, "the user's new sign-in name, e-mail, password or places");
            }

            switch (what)
            {
                case "READ":
                    break;
                case "USERNAME":
                    await access.SetUsernameAsync(id, new UsernameChange(value ?? throw new McpException("Give the new sign-in name.")), ct).ConfigureAwait(false);
                    break;
                case "EMAIL":
                    await access.SetEmailAsync(id, new UserEmail(string.IsNullOrWhiteSpace(value) ? null : value.Trim()), ct).ConfigureAwait(false);
                    break;
                case "PASSWORD":
                    await access.ResetPasswordAsync(id, new PasswordReset(value ?? throw new McpException("Give the new first password.")), ct).ConfigureAwait(false);
                    break;
                case "PLACES":
                    var ids = new List<Guid>();
                    foreach (var place in places ?? throw new McpException("Give the places."))
                    {
                        ids.Add(await Registers.ElementAsync(s => Registers.CostCenters(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct),
                            place, "cost centre").ConfigureAwait(false));
                    }

                    await access.SetCostCentersAsync(id, ids, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new McpException("The action is read, username, email, password or places.");
            }

            var signIn = await access.SignInAsync(id, ct).ConfigureAwait(false);
            return new { user = id, signIn.Username, signIn.Email, places = await access.CostCentersAsync(id, ct).ConfigureAwait(false), passwordChanged = what == "PASSWORD" };
        });

    [McpServerTool(Name = "my_account", Title = "My rights, menu, language and password", ReadOnly = false, Destructive = false)]
    [Description("The signed-in user's own account: action rights (whether an administrator, and the rights per entity and category), menu (the company's menu as this user sees it), language (sets the user's screen language, e.g. en, it, sq), or password (changes the user's own password: the current one and the new one; never repeat them back). Language and password need the user's explicit yes; then confirm=true.")]
    public Task<object> MyAccountAsync(
        [Description("rights, menu, language or password.")] string action,
        [Description("language: the language code; password: the new password.")] string? value = null,
        [Description("password: the current password.")] string? current = null,
        [Description("True only after the user said yes (language, password).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            switch (action.Trim().ToUpperInvariant())
            {
                case "RIGHTS":
                    return await access.MyRightsAsync(ct).ConfigureAwait(false);
                case "MENU":
                    var (menu, _) = await visibility.MenuAsync(ct).ConfigureAwait(false);
                    return menu.Select(m => new { m.Id, parentId = m.IdSup, m.Code, name = m.Name?.In(Registers.English), isScreen = m.IsElement });
                case "LANGUAGE":
                    Erp.NeedsYes(confirm, "the new screen language");
                    await access.SetLanguageAsync(new LanguageChoice(value), ct).ConfigureAwait(false);
                    return new { language = value };
                case "PASSWORD":
                    Erp.NeedsYes(confirm, "that the password will be changed");
                    await access.ChangePasswordAsync(new PasswordChange(current ?? throw new McpException("Give the current password."),
                        value ?? throw new McpException("Give the new password.")), ct).ConfigureAwait(false);
                    return new { passwordChanged = true };
                default:
                    throw new McpException("The action is rights, menu, language or password.");
            }
        });

    // ---------- Personal tokens ----------

    [McpServerTool(Name = "tokens", Title = "Personal tokens", ReadOnly = false, Destructive = true)]
    [Description("Personal tokens (Claude or a script signs in with one as its user): action list (the user's tokens: name, made, last used, switched off), make (a new token for the signed-in user; its value is shown once in the answer: hand it over and never repeat it later), or revoke (switches a token off for good). With a user, an administrator lists or revokes that user's tokens. Make and revoke need an explicit yes; then confirm=true.")]
    public Task<object> TokensAsync(
        [Description("list, make or revoke.")] string action,
        [Description("make: the token's name (e.g. 'Claude on the laptop').")] string? name = null,
        [Description("revoke: the token's id.")] Guid? token = null,
        [Description("Optional, administrators: another user (id or exact code).")] string? user = null,
        [Description("True only after the user said yes (make, revoke).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            Guid? userId = user is null ? null
                : await Registers.ElementAsync(s => Registers.Users(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), user, "user").ConfigureAwait(false);
            switch (action.Trim().ToUpperInvariant())
            {
                case "LIST":
                    return userId is { } u ? await tokens.OfUserAsync(u, ct).ConfigureAwait(false) : await tokens.MineAsync(ct).ConfigureAwait(false);
                case "MAKE":
                    Erp.NeedsYes(confirm, "the new token's name (its value is shown once)");
                    if (userId is not null)
                    {
                        throw new McpException("A token is made only by its own user.");
                    }

                    return await tokens.MakeAsync(new NewUserToken(name), ct).ConfigureAwait(false);
                case "REVOKE":
                    Erp.NeedsYes(confirm, "the token to switch off for good");
                    var id = token ?? throw new McpException("Give the token's id.");
                    if (userId is { } other)
                    {
                        await tokens.RevokeOfUserAsync(other, id, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await tokens.RevokeMineAsync(id, ct).ConfigureAwait(false);
                    }

                    return new { revoked = id };
                default:
                    throw new McpException("The action is list, make or revoke.");
            }
        });

    // ---------- User activity ----------

    [McpServerTool(Name = "audit", Title = "Who did what", ReadOnly = true)]
    [Description("The User activity screen: what=users (the users who can be followed), sign-ins (sign-ins, sign-outs and wrong passwords in a period, optionally of one user), activity (per table, how many rows each user added, changed or switched off in a period), rows (one table's changed rows in a period), or change (one row's fields before and after, by table and the history row's id).")]
    public Task<object> AuditAsync(
        [Description("users, sign-ins, activity, rows or change.")] string what,
        [Description("From (yyyy-mm-dd).")] DateOnly? from = null,
        [Description("To (yyyy-mm-dd).")] DateOnly? to = null,
        [Description("Optional: one user (id or exact code).")] string? user = null,
        [Description("rows, change: the table (as activity lists it).")] string? table = null,
        [Description("change: the row's id.")] Guid? id = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            Guid? userId = user is null ? null
                : await Registers.ElementAsync(s => Registers.Users(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), user, "user").ConfigureAwait(false);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var (f, t) = (from ?? today.AddDays(-7), to ?? today);
            return what.Trim().ToUpperInvariant() switch
            {
                "USERS" => await audit.UsersAsync(ct).ConfigureAwait(false),
                "SIGN-INS" or "SIGNINS" => await audit.SignInsAsync(f, t, userId, ct).ConfigureAwait(false),
                "ACTIVITY" => await audit.ActivityAsync(f, t, userId, ct).ConfigureAwait(false),
                "ROWS" => await audit.RowsAsync(table ?? throw new McpException("Give the table."), f, t, userId, Registers.English, ct).ConfigureAwait(false),
                "CHANGE" => (object)await audit.ChangeAsync(table ?? throw new McpException("Give the table."), id ?? throw new McpException("Give the row's id."), Registers.English, ct)
                    .ConfigureAwait(false),
                _ => throw new McpException("What is users, sign-ins, activity, rows or change."),
            };
        });

    // ---------- Skins ----------

    [McpServerTool(Name = "save_skin", Title = "Upload, read or switch off a skin", ReadOnly = false, Destructive = false)]
    [Description("The company's skins beyond list_skins and use_skin: action in-use (the skin in use for erp or pos), file (a skin's tokens, as a skin file), upload (a new skin: name, kind erp or pos, dark or not, its design tokens such as --accent: #4fb7e8), built-in (back to the built-in skin of a kind), or switch-off (a skin not in use). Administrators. Changes need an explicit yes; then confirm=true.")]
    public Task<object> SaveSkinAsync(
        [Description("in-use, file, upload, built-in or switch-off.")] string action,
        [Description("file, switch-off: the skin's id.")] Guid? skin = null,
        [Description("upload: the skin's name.")] string? name = null,
        [Description("in-use, upload, built-in: erp or pos.")] string? kind = null,
        [Description("upload: true for a dark skin.")] bool dark = false,
        [Description("upload: the design tokens, token → value.")] Dictionary<string, string>? designTokens = null,
        [Description("True only after the administrator said yes (upload, built-in, switch-off).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            switch (action.Trim().ToUpperInvariant())
            {
                case "IN-USE":
                    return await skins.InUseAsync(kind ?? "erp", ct).ConfigureAwait(false);
                case "FILE":
                    return await skins.FileAsync(skin ?? throw new McpException("Give the skin."), ct).ConfigureAwait(false);
                case "UPLOAD":
                    Erp.NeedsYes(confirm, "the skin to upload");
                    return await skins.UploadAsync(new SkinFile(name, kind, dark, designTokens), ct).ConfigureAwait(false);
                case "BUILT-IN":
                    Erp.NeedsYes(confirm, "going back to the built-in skin");
                    await skins.UseBuiltInAsync(kind ?? "erp", ct).ConfigureAwait(false);
                    return new { builtIn = kind ?? "erp" };
                case "SWITCH-OFF":
                    Erp.NeedsYes(confirm, "the skin to switch off");
                    await skins.DeleteAsync(skin ?? throw new McpException("Give the skin."), ct).ConfigureAwait(false);
                    return new { switchedOff = skin };
                default:
                    throw new McpException("The action is in-use, file, upload, built-in or switch-off.");
            }
        });

    // ---------- Group, statutory chart, starter packs ----------

    [McpServerTool(Name = "group_setup", Title = "A group's books, the statutory chart and the starter packs", ReadOnly = false, Destructive = false)]
    [Description("Set-up at the group and the chart: action packs (the activity profiles, the countries with a starter pack and the pack's groups), books-needed (whether the group still needs its own books), books (gives the group its own books once, from a country's pack, optionally only some groups), link-partners (makes each company of the group a partner of the others for inter-company documents), statutory (which chart category the reports read), or set-statutory (marks a chart category statutory or not). Administrators. Changes need an explicit yes; then confirm=true.")]
    public Task<object> GroupSetupAsync(
        [Description("packs, books-needed, books, link-partners, statutory or set-statutory.")] string action,
        [Description("books: the country (ISO code).")] string? country = null,
        [Description("books: optional, only these pack groups.")] IReadOnlyList<string>? groups = null,
        [Description("set-statutory: the chart category (code, name or id).")] string? chart = null,
        [Description("set-statutory: true or false.")] bool statutoryChart = true,
        [Description("True only after the administrator said yes (books, link-partners, set-statutory).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            switch (action.Trim().ToUpperInvariant())
            {
                case "PACKS":
                    return new
                    {
                        activities = Seed.ActivityCatalogue.All, countries = SetupService.Countries(),
                        groups = Seed.StarterPacks.Groups.Select(g => new { name = g.Key, requires = g.Value.Requires }),
                    };
                case "BOOKS-NEEDED":
                    return new { needed = await setup.GroupBooksNeededAsync(request, ct).ConfigureAwait(false) };
                case "BOOKS":
                    Erp.NeedsYes(confirm, "the group's own books from the country's pack");
                    return new { rows = await setup.SetUpGroupBooksAsync(new GroupBooksRequest(country ?? throw new McpException("Give the country."), groups), request, ct).ConfigureAwait(false) };
                case "LINK-PARTNERS":
                    Erp.NeedsYes(confirm, "the group's companies made partners of each other");
                    return new { rows = await setup.LinkGroupPartnersAsync(request, ct).ConfigureAwait(false) };
                case "STATUTORY":
                    Registers.Administrator(request);
                    return await statutory.StatutoryAsync(ct).ConfigureAwait(false);
                case "SET-STATUTORY":
                    Erp.NeedsYes(confirm, "the chart category marked statutory or not");
                    Registers.Administrator(request);
                    var category = Registers.Category(await Registers.Charts(db, request, visibility).CategoriesAsync(ct).ConfigureAwait(false),
                        chart ?? throw new McpException("Give the chart category."), "chart")!.Value;
                    return await statutory.SetAsync(category, statutoryChart, ct).ConfigureAwait(false);
                default:
                    throw new McpException("The action is packs, books-needed, books, link-partners, statutory or set-statutory.");
            }
        });

    // ---------- Document types' settings ----------

    [McpServerTool(Name = "document_type_settings", Title = "A document type's settings", ReadOnly = false, Destructive = false)]
    [Description("A document type's General settings, as System settings › Document types does: read them (never posted, partial reversal, is a reversal and of which type, sends to production and with which type, sells the produced product, starts a repair, can be accepted, holds or pays open items, bills by plan, needs approval, sells assets, produced on the Production screen) and who it is given to; or set them (only the settings given change). Setting needs the administrator's explicit yes; then confirm=true.")]
    public Task<object> DocumentTypeSettingsAsync(
        [Description("The document type: id, code or name.")] string type,
        [Description("Optional: the settings to change, name → value (e.g. NeedsApproval: true, ReversedDocumentTypeId: an id).")] Dictionary<string, System.Text.Json.JsonElement>? set = null,
        [Description("True only after the administrator said yes (set).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var typeDto = Registers.Type(await documents.Entities.TypesAsync(ct).ConfigureAwait(false), type, "document");
            var id = typeDto.Id!.Value;
            var settings = await configuration.DocumentTypeAsync(id, ct).ConfigureAwait(false) ?? throw new McpException("The document type is not found.");
            if (set is { Count: > 0 })
            {
                Erp.NeedsYes(confirm, "the document type's new settings");
                var node = System.Text.Json.JsonSerializer.SerializeToNode(settings, Json)!.AsObject();
                foreach (var (key, value) in set)
                {
                    var name = node.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        ?? throw new McpException($"There is no setting {key}. Settings: {string.Join(", ", node.Select(p => p.Key))}.");
                    node[name] = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText());
                }

                settings = await configuration.SetDocumentTypeAsync(id, node.Deserialize<DocumentTypeSettings>(Json)!, ct).ConfigureAwait(false);
            }

            return new { type = typeDto.Code ?? Registers.Name(typeDto.Name), settings, users = await configuration.DocumentTypeUsersAsync(id, ct).ConfigureAwait(false) };
        });

    // ---------- A document's details ----------

    [McpServerTool(Name = "document_details", Title = "A document's details", ReadOnly = true)]
    [Description("More about one document, as its editor shows it: what=posting-preview (the entries posting it would make, before it is posted), history (its earlier versions: who changed what, when), neighbours (the previous and next document of its type), reversible (what is left to reverse per line), journals (a sheet's rows as documents), flows (what it can make), made (the documents made from it), or pdf (the printout, as base64, in a language). For a line's proposed unit, price and VAT group, give what=line-proposal with type and product.")]
    public Task<object> DocumentDetailsAsync(
        [Description("posting-preview, history, neighbours, reversible, journals, flows, made, pdf or line-proposal.")] string what,
        [Description("The document's id (not for line-proposal).")] Guid? id = null,
        [Description("line-proposal: the document type (id, code or name).")] string? type = null,
        [Description("line-proposal: the product (id or exact code).")] string? product = null,
        [Description("line-proposal: optional, the partner (id or exact code).")] string? partner = null,
        [Description("pdf: the language (e.g. en, it); English when left out.")] string? language = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var lang = string.IsNullOrWhiteSpace(language) ? Registers.English : language.Trim();
            Guid Id() => id ?? throw new McpException("Give the document's id.");
            switch (what.Trim().ToLowerInvariant())
            {
                case "posting-preview":
                    return (object?)await documents.PostingPreviewAsync(Id(), ct).ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.");
                case "history":
                    return await documents.Entities.HistoryAsync(Id(), ct).ConfigureAwait(false);
                case "neighbours":
                    return (object?)await documents.NeighboursAsync(Id(), ct).ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.");
                case "reversible":
                    return await documents.ReversibleAsync(Id(), ct).ConfigureAwait(false);
                case "journals":
                    return await documents.JournalsAsync(Id(), ct).ConfigureAwait(false);
                case "flows":
                    return await documents.FlowsAsync(Id(), lang, ct).ConfigureAwait(false);
                case "made":
                    return await documents.MadeFromAsync(Id(), lang, ct).ConfigureAwait(false);
                case "pdf":
                    var pdf = await print.DocumentAsync(Id(), lang, ct).ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.");
                    return new { pdf.FileName, contentType = "application/pdf", base64 = Convert.ToBase64String(pdf.Content) };
                case "line-proposal":
                    var typeDto = Registers.Type(await documents.AllowedTypesAsync(null, ct).ConfigureAwait(false), type ?? throw new McpException("Give the document type."), "document");
                    var productId = await Registers.ElementAsync(s => Registers.Products(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct),
                        product ?? throw new McpException("Give the product."), "product").ConfigureAwait(false);
                    Guid? partnerId = partner is null ? null
                        : await Registers.ElementAsync(s => Registers.Partners(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), partner, "partner")
                            .ConfigureAwait(false);
                    return await documents.LineProposalAsync(typeDto.Id!.Value, productId, partnerId, null, null, lang, ct).ConfigureAwait(false);
                default:
                    throw new McpException("What is posting-preview, history, neighbours, reversible, journals, flows, made, pdf or line-proposal.");
            }
        });

    [McpServerTool(Name = "release_all_documents", Title = "Let go of every document I hold", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Lets go of every document the signed-in user holds (as signing out does), so others may change them.")]
    public Task<object> ReleaseAllAsync(CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await documents.ReleaseAllAsync(ct).ConfigureAwait(false);
            return new { released = true };
        });

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);
}

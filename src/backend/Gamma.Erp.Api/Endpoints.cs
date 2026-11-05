using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Depreciation;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Partners;
using Gamma.Erp.Services.Posting;
using Charts = Gamma.Erp.Models.Charts;
using CostCenters = Gamma.Erp.Models.CostCenters;
using Documents = Gamma.Erp.Models.Documents;
using Employees = Gamma.Erp.Models.Employees;
using Opportunities = Gamma.Erp.Models.Opportunities;
using Partners = Gamma.Erp.Models.Partners;
using Products = Gamma.Erp.Models.Products;
using Transactions = Gamma.Erp.Models.Transactions;
using Units = Gamma.Erp.Models.Units;
using Users = Gamma.Erp.Models.Users;

namespace Gamma.Erp.Api;

/// <summary>REST /api/v1 over the services. Every route needs a signed-in user; the services apply the node, the rights and the rules.</summary>
public static class Endpoints
{
    public sealed record ReverseRequest(DateOnly PostingDate, Dictionary<Guid, decimal>? Quantities, Dictionary<Guid, IReadOnlyList<string>>? Serials = null);

    public sealed record JournalRequest(DateOnly Date, Guid DocumentTypeId, Guid CategoryId);

    public sealed record ContractPlanRequest(int? EveryMonths, DateOnly? NextDate);

    public sealed record OpportunityMove(Guid StageId, string? LostReason);

    public static void MapApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var api = app.MapGroup("/api/v1").RequireAuthorization();

        // What a new company of a group is started from: the activity catalogue, the countries with a pack, the pack groups. A new customer is made on the server only.
        api.MapGet("/setup/activities", () => Seed.ActivityCatalogue.All).WithTags("setup");
        api.MapGet("/setup/countries", () => SetupService.Countries()).WithTags("setup");
        api.MapGet("/setup/groups", () => Seed.StarterPacks.Groups.Select(g => new { name = g.Key, requires = g.Value.Requires })).WithTags("setup");

        MapEntity<Partners.PartnerCategory, Partners.PartnerType, Partners.InterPartnerTypeCategory, Partners.PartnerTypeInfo, Partners.PartnerTypeInfoChoice,
            Partners.Partner, Partners.PartnerInfo, Partners.InterPartnerCategory, Partners.PartnerAccess>(api, "partners");
        MapEntity<Products.ProductCategory, Products.ProductType, Products.InterProductTypeCategory, Products.ProductTypeInfo, Products.ProductTypeInfoChoice,
            Products.Product, Products.ProductInfo, Products.InterProductCategory, Products.ProductAccess>(api, "products");
        MapEntity<CostCenters.CostCenterCategory, CostCenters.CostCenterType, CostCenters.InterCostCenterTypeCategory, CostCenters.CostCenterTypeInfo,
            CostCenters.CostCenterTypeInfoChoice, CostCenters.CostCenter, CostCenters.CostCenterInfo, CostCenters.InterCostCenterCategory, CostCenters.CostCenterAccess>(api, "cost-centers",
            check: db => CostCenterRules.CompanyLevel(db));
        MapEntity<Charts.ChartCategory, Charts.ChartType, Charts.InterChartTypeCategory, Charts.ChartTypeInfo, Charts.ChartTypeInfoChoice,
            Charts.Chart, Charts.ChartInfo, Charts.InterChartCategory, Charts.ChartAccess>(api, "charts");
        // Which chart category is the statutory one (the chart the reports read; the account mapping maps the others into it).
        api.MapGet("/charts/categories/statutory", (Gamma.Erp.Services.Configuration.StatutoryChartService s, CancellationToken ct) => s.StatutoryAsync(ct))
            .WithTags("charts").RequireAdministrator();
        api.MapPost("/charts/categories/{id:guid}/statutory", (Gamma.Erp.Services.Configuration.StatutoryChartService s, Guid id, bool statutory, CancellationToken ct) =>
            s.SetAsync(id, statutory, ct)).WithTags("charts").RequireAdministrator();
        MapEntity<Units.UnitCategory, Units.UnitType, Units.InterUnitTypeCategory, Units.UnitTypeInfo, Units.UnitTypeInfoChoice,
            Units.Unit, Units.UnitInfo, Units.InterUnitCategory, Units.UnitAccess>(api, "units");
        MapEntity<Employees.EmployeeCategory, Employees.EmployeeType, Employees.InterEmployeeTypeCategory, Employees.EmployeeTypeInfo, Employees.EmployeeTypeInfoChoice,
            Employees.Employee, Employees.EmployeeInfo, Employees.InterEmployeeCategory, Employees.EmployeeAccess>(api, "employees");
        MapEntity<Users.UserCategory, Users.UserType, Users.InterUserTypeCategory, Users.UserTypeInfo, Users.UserTypeInfoChoice,
            Users.User, Users.UserInfo, Users.InterUserCategory, Users.UserAccess>(api, "users");
        MapEntity<Transactions.TransactionCategory, Transactions.TransactionType, Transactions.InterTransactionTypeCategory, Transactions.TransactionTypeInfo,
            Transactions.TransactionTypeInfoChoice, Transactions.Transaction, Transactions.TransactionInfo, Transactions.InterTransactionCategory,
            Transactions.TransactionAccess>(api, "transactions", elements: false);

        MapEntity<Opportunities.OpportunityCategory, Opportunities.OpportunityType, Opportunities.InterOpportunityTypeCategory, Opportunities.OpportunityTypeInfo,
            Opportunities.OpportunityTypeInfoChoice, Opportunities.Opportunity, Opportunities.OpportunityInfo, Opportunities.InterOpportunityCategory, Opportunities.OpportunityAccess>(api, "opportunities");

        LookupEndpoints.MapLookups(api);

        // The signed-in user's menu: the company's tree, the nodes given to the user or their roles, each documents item with its types.
        api.MapGet("/menu", (MenuService s, string? language, CancellationToken ct) => s.MenuAsync(language ?? "en", ct)).WithTags("menu");

        // Documents: their categories and types as every entity's; the documents themselves through the document service.
        var documents = api.MapGroup("/documents").WithTags("documents");
        documents.MapGet("/categories", (DocumentService s, CancellationToken ct) => s.Entities.CategoriesAsync(ct));
        documents.MapPost("/categories", (DocumentService s, CategoryDto dto, CancellationToken ct) => s.Entities.SaveCategoryAsync(dto, ct)).RequireScreen("document-types");
        documents.MapGet("/types", (DocumentService s, CancellationToken ct) => s.Entities.TypesAsync(ct));
        documents.MapPost("/types", (DocumentService s, ElementTypeDto dto, CancellationToken ct) => s.Entities.SaveTypeAsync(dto, ct)).RequireScreen("document-types");
        documents.MapGet("/types/allowed", (DocumentService s, Guid? category, CancellationToken ct) => s.AllowedTypesAsync(category, ct));
        documents.MapPut("/types/{id:guid}/users", async (DocumentService s, Guid id, DocumentTypeUsersRequest body, CancellationToken ct) =>
        {
            await s.SaveTypeUsersAsync(id, body.UserIds ?? [], body.RoleIds ?? [], ct);
            return Results.NoContent();
        });
        documents.MapGet("/", (DocumentService s, [AsParameters] DocumentListParameters p, CancellationToken ct) => s.ListAsync(p.Query(), ct));
        // What the document editor needs: a type's form, and a line's units and proposed price once its product is picked.
        documents.MapGet("/types/{id:guid}/form", (DocumentService s, Guid id, string? language, CancellationToken ct) => s.FormAsync(id, language ?? "en", ct));
        documents.MapGet("/payment-methods", (Gamma.Erp.Services.Pos.PosService s, Guid costCenter, CancellationToken ct) => s.MethodsAtAsync(costCenter, ct));
        documents.MapGet("/line-proposal", (DocumentService s, Guid documentType, Guid product, Guid? partner, Guid? unit, Guid? currency, DateOnly? date, string? language,
            CancellationToken ct) => s.LineProposalAsync(documentType, product, partner, unit, currency, language ?? "en", ct, date));
        documents.MapPost("/{id:guid}/repeat", async (DocumentService s, Guid id, RepeatRequest body, CancellationToken ct) =>
            await s.RepeatAsync(id, body.Date, body.Post, ct) is { } d ? Results.Ok(d) : Results.NotFound());
        documents.MapGet("/{id:guid}/neighbours", async (DocumentService s, Guid id, CancellationToken ct) =>
            await s.NeighboursAsync(id, ct) is { } n ? Results.Ok(n) : Results.NotFound());
        documents.MapGet("/{id:guid}/posting-preview", async (DocumentService s, Guid id, CancellationToken ct) =>
            await s.PostingPreviewAsync(id, ct) is { } rows ? Results.Ok(rows) : Results.NotFound());
        documents.MapGet("/{id:guid}", async (DocumentService s, Guid id, CancellationToken ct) => await s.DocumentAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound());
        documents.MapPost("/", (DocumentService s, DocumentDto dto, CancellationToken ct) => s.SaveAsync(dto, ct));
        documents.MapDelete("/{id:guid}", async (DocumentService s, Guid id, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); });
        documents.MapGet("/{id:guid}/flows", (DocumentService s, Guid id, string? language, CancellationToken ct) => s.FlowsAsync(id, language ?? "en", ct));
        documents.MapGet("/{id:guid}/made", (DocumentService s, Guid id, string? language, CancellationToken ct) => s.MadeFromAsync(id, language ?? "en", ct));
        documents.MapPost("/{id:guid}/make/{type:guid}", (DocumentService s, Guid id, Guid type, CancellationToken ct) => s.MakeAsync(id, type, ct));
        documents.MapGet("/{id:guid}/journals", (DocumentService s, Guid id, CancellationToken ct) => s.JournalsAsync(id, ct));
        documents.MapPost("/sheet", (DocumentService s, SheetDto sheet, CancellationToken ct) => s.SaveSheetAsync(sheet, ct));
        documents.MapPost("/{id:guid}/post", (DocumentService s, Guid id, CancellationToken ct) => s.PostAsync(id, ct));
        documents.MapGet("/{id:guid}/reversible", (DocumentService s, Guid id, CancellationToken ct) => s.ReversibleAsync(id, ct));
        documents.MapPost("/{id:guid}/reverse", (DocumentService s, Guid id, ReverseRequest body, CancellationToken ct) => s.ReverseAsync(id, body.Quantities, body.Serials, body.PostingDate, ct));
        documents.MapPost("/{id:guid}/hold", async (DocumentService s, Guid id, CancellationToken ct) => { await s.HoldAsync(id, ct); return Results.NoContent(); });
        documents.MapPost("/{id:guid}/release", async (DocumentService s, Guid id, CancellationToken ct) => { await s.ReleaseAsync(id, ct); return Results.NoContent(); });
        // On sign-out: every draft the user holds is let go.
        documents.MapPost("/release-all", async (DocumentService s, CancellationToken ct) => { await s.ReleaseAllAsync(ct); return Results.NoContent(); });
        documents.MapGet("/{id:guid}/holder", async (DocumentService s, Guid id, CancellationToken ct) => Results.Ok(new { holder = await s.HolderAsync(id, ct) }));
        documents.MapGet("/{id:guid}/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid id, string? language, CancellationToken ct) =>
            await s.DocumentAsync(id, language ?? "en", ct) is { } pdf ? Results.File(pdf.Content, "application/pdf", pdf.FileName) : Results.NotFound());
        documents.MapPost("/{id:guid}/email", async (Gamma.Erp.Services.Mail.MailService s, Guid id, Gamma.Erp.Services.Mail.SendDocumentRequest body, CancellationToken ct) =>
            await s.SendDocumentAsync(id, body, ct) is { } sent ? Results.Ok(new { messageId = sent }) : Results.NotFound());
        documents.MapGet("/{id:guid}/history", (DocumentService s, Guid id, CancellationToken ct) => s.Entities.HistoryAsync(id, ct));

        // Posting configuration: the company's scenarios that cannot balance.
        // A further company of a group, from its country's pack or copied from another company of the group.
        // A group's own books (the founder, 2026-10-31: a group behaves as a company): whether it still needs them, and setting them up once.
        // Personal tokens (the founder, 2026-11-04): Claude or a script signs in with one as its user. Each user handles their own; an administrator anyone's.
        api.MapGet("/account/tokens", (Gamma.Erp.Api.Auth.TokenService s, CancellationToken ct) => s.MineAsync(ct)).WithTags("account");
        api.MapPost("/account/tokens", (Gamma.Erp.Api.Auth.TokenService s, Gamma.Erp.Api.Auth.NewUserToken made, CancellationToken ct) => s.MakeAsync(made, ct)).WithTags("account");
        api.MapDelete("/account/tokens/{id:guid}", async (Gamma.Erp.Api.Auth.TokenService s, Guid id, CancellationToken ct) => { await s.RevokeMineAsync(id, ct); return Results.NoContent(); })
            .WithTags("account");
        api.MapGet("/users/{userId:guid}/tokens", (Gamma.Erp.Api.Auth.TokenService s, Guid userId, CancellationToken ct) => s.OfUserAsync(userId, ct)).WithTags("access");
        api.MapDelete("/users/{userId:guid}/tokens/{id:guid}", async (Gamma.Erp.Api.Auth.TokenService s, Guid userId, Guid id, CancellationToken ct) =>
        {
            await s.RevokeOfUserAsync(userId, id, ct);
            return Results.NoContent();
        }).WithTags("access");

        // Skins (the founder, 2026-11-04): the one in use is read by everyone signed in; listing, uploading and choosing are for administrators.
        var skins = api.MapGroup("/skins").WithTags("skins");
        skins.MapGet("/in-use", (Gamma.Erp.Services.Platform.SkinService s, string? kind, CancellationToken ct) => s.InUseAsync(kind ?? "erp", ct));
        skins.MapGet("/", (Gamma.Erp.Services.Platform.SkinService s, CancellationToken ct) => s.ListAsync(ct));
        skins.MapGet("/{id:guid}/file", (Gamma.Erp.Services.Platform.SkinService s, Guid id, CancellationToken ct) => s.FileAsync(id, ct));
        skins.MapPost("/", (Gamma.Erp.Services.Platform.SkinService s, Gamma.Erp.Services.Platform.SkinFile file, CancellationToken ct) => s.UploadAsync(file, ct));
        skins.MapPost("/{id:guid}/use", (Gamma.Erp.Services.Platform.SkinService s, Guid id, CancellationToken ct) => s.UseAsync(id, ct));
        skins.MapPost("/built-in", async (Gamma.Erp.Services.Platform.SkinService s, string kind, CancellationToken ct) => { await s.UseBuiltInAsync(kind, ct); return Results.NoContent(); });
        skins.MapDelete("/{id:guid}", async (Gamma.Erp.Services.Platform.SkinService s, Guid id, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); });

        api.MapGet("/group-books", async (SetupService s, RequestContext context, CancellationToken ct) =>
            Results.Ok(new { needed = await s.GroupBooksNeededAsync(context, ct) })).WithTags("setup");
        api.MapPost("/group-books", async (SetupService s, RequestContext context, GroupBooksRequest request, CancellationToken ct) =>
            Results.Ok(new { rows = await s.SetUpGroupBooksAsync(request, context, ct) })).WithTags("setup");
        api.MapPost("/group-books/partners", async (SetupService s, RequestContext context, CancellationToken ct) =>
            Results.Ok(new { rows = await s.LinkGroupPartnersAsync(context, ct) })).WithTags("setup");
        api.MapPost("/companies", async (SetupService s, RequestContext context, CompanyRequest request, CancellationToken ct) =>
            Results.Created("/api/v1/cost-centers", await s.AddCompanyAsync(request, context, ct))).WithTags("setup");

        api.MapGet("/posting/unbalanced-scenarios", async (PostingService s, Visibility v, string? language, CancellationToken ct) =>
            await s.UnbalancedListAsync(await v.CompanyAsync(ct), language ?? "en", ct)).WithTags("posting").RequireReport("unbalanced-scenarios");

        // Depreciation.
        // Reports: the accounts' language is the one asked for, else the chart's own (each country's chart is in its language).
        var reports = api.MapGroup("/reports").WithTags("reports");
        reports.MapGet("/trial-balance", (Gamma.Erp.Services.Reports.ReportService s, DateOnly from, DateOnly to, Guid? chart, string? language, Guid? costCenter,
            CancellationToken ct) => s.TrialBalanceAsync(from, to, chart, language ?? "en", costCenter, ct)).RequireReport("trial-balance");
        reports.MapGet("/ledger", async (Gamma.Erp.Services.Reports.ReportService s, Guid account, DateOnly from, DateOnly to, string? language, Guid? costCenter,
            CancellationToken ct) => await s.LedgerAsync(account, from, to, language ?? "en", costCenter, ct) is { } l ? Results.Ok(l) : Results.NotFound()).RequireReport("ledger");
        reports.MapGet("/vat-register", (Gamma.Erp.Services.Reports.ReportService s, Guid[] category, DateOnly from, DateOnly to, string? language, CancellationToken ct) =>
            s.VatRegisterAsync(category, from, to, language ?? "en", ct)).RequireReport("vat-register");
        reports.MapGet("/document-totals", (Gamma.Erp.Services.Reports.ReportService s, Guid[] category, DateOnly from, DateOnly to, string? language, CancellationToken ct) =>
            s.DocumentTotalsAsync(category, from, to, language ?? "en", ct)).RequireReport("document-totals");
        reports.MapGet("/contracts", (Gamma.Erp.Services.Reports.ReportService s, Guid[] category, Guid? partner, DateOnly from, DateOnly to, string? language, CancellationToken ct) =>
            s.ContractsAsync(category, partner, from, to, language ?? "en", ct)).RequireReport("contracts");
        reports.MapGet("/balance-sheet", (Gamma.Erp.Services.Reports.ReportService s, DateOnly to, Guid? chart, string? language, CancellationToken ct) => s.BalanceSheetAsync(to, chart, language ?? "en", ct)).RequireReport("balance-sheet");
        reports.MapGet("/profit-loss", (Gamma.Erp.Services.Reports.ReportService s, DateOnly from, DateOnly to, Guid? chart, string? language, CancellationToken ct) => s.ProfitLossAsync(from, to, chart, language ?? "en", ct)).RequireReport("profit-loss");
        reports.MapGet("/vat-return/form", (Gamma.Erp.Services.Reports.ReportService s, DateOnly from, DateOnly to, string? language, CancellationToken ct) =>
            s.VatReturnFormAsync(from, to, language ?? "en", ct)).RequireReport("vat-return-form");
        reports.MapGet("/vat-return", (Gamma.Erp.Services.Reports.ReportService s, Guid[] output, Guid[] input, DateOnly from, DateOnly to, string? language, CancellationToken ct) =>
            s.VatReturnAsync(output, input, from, to, language ?? "en", ct)).RequireReport("vat-return");

        // The reports as PDF, with the same parameters.
        reports.MapGet("/trial-balance/pdf", async (Gamma.Erp.Services.Printing.PrintService s, DateOnly from, DateOnly to, Guid? chart, string? language,
            Guid? costCenter, CancellationToken ct) => Pdf(await s.TrialBalanceAsync(from, to, chart, language ?? "en", costCenter, ct))).RequireReport("trial-balance");
        reports.MapGet("/ledger/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid account, DateOnly from, DateOnly to, string? language,
            Guid? costCenter, CancellationToken ct) => await s.LedgerAsync(account, from, to, language ?? "en", costCenter, ct) is { } pdf ? Pdf(pdf) : Results.NotFound()).RequireReport("ledger");
        reports.MapGet("/vat-register/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid[] category, DateOnly from, DateOnly to, string? language, CancellationToken ct) =>
            Pdf(await s.VatRegisterAsync(category, from, to, language ?? "en", ct))).RequireReport("vat-register");
        reports.MapGet("/vat-return/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid[] output, Guid[] input, DateOnly from, DateOnly to, string? language,
            CancellationToken ct) => Pdf(await s.VatReturnAsync(output, input, from, to, language ?? "en", ct))).RequireReport("vat-return");

        api.MapGet("/cost-centers/{id:guid}/balance", async (Gamma.Erp.Services.Reports.ReportService s, Guid id, DateOnly? until, CancellationToken ct) =>
            await s.CostCenterBalanceAsync(id, until, ct) is { } b ? Results.Ok(b) : Results.NotFound()).WithTags("cost-centers");
        api.MapGet("/employees/{id:guid}/ledger", async (Gamma.Erp.Services.Reports.ReportService s, Guid id, DateOnly from, DateOnly to, CancellationToken ct) =>
            await s.EmployeeLedgerAsync(id, from, to, ct) is { } l ? Results.Ok(l) : Results.NotFound()).WithTags("employees").RequireReport("staff-ledger");
        api.MapGet("/cost-centers/{id:guid}/ledger", async (Gamma.Erp.Services.Reports.ReportService s, Guid id, DateOnly from, DateOnly to, CancellationToken ct) =>
            await s.CostCenterLedgerAsync(id, from, to, ct) is { } l ? Results.Ok(l) : Results.NotFound()).WithTags("cost-centers").RequireReport("cost-center-ledger");
        api.MapGet("/cost-centers/{id:guid}/ledger/xlsx", async (Gamma.Erp.Services.Reports.ReportService s, Guid id, DateOnly from, DateOnly to, CancellationToken ct) =>
            await s.CostCenterLedgerAsync(id, from, to, ct) is { } l ? Xlsx(l, "cost-centre-ledger") : Results.NotFound()).WithTags("cost-centers").RequireReport("cost-center-ledger");
        api.MapGet("/cost-centers/{id:guid}/ledger/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid id, DateOnly from, DateOnly to, string? language,
            CancellationToken ct) => await s.CostCenterLedgerAsync(id, from, to, language ?? "en", ct) is { } pdf ? Pdf(pdf) : Results.NotFound()).WithTags("cost-centers").RequireReport("cost-center-ledger");

        // The company's configuration rows (the engine's rules), read and written as plain JSON; the query string filters by column.
        var config = api.MapGroup("/config").WithTags("configuration");
        config.MapGet("/", () => Gamma.Erp.Services.Configuration.ConfigurationService.Models.Keys.Order());
        config.MapGet("/{model}", (Gamma.Erp.Services.Configuration.ConfigurationService s, HttpContext http, string model, CancellationToken ct) =>
            s.ListAsync(model, http.Request.Query.Where(q => q.Key != "language").ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase), ct));
        // What a screen needs to edit a model (B6): its columns, and the rows a link column may point at.
        config.MapGet("/{model}/schema", (Gamma.Erp.Services.Configuration.ConfigurationService s, string model) => s.Schema(model));
        config.MapGet("/{model}/choices/{column}", (Gamma.Erp.Services.Configuration.ConfigurationService s, string model, string column, string? search,
            string? language, CancellationToken ct) => s.ChoicesAsync(model, column, search, language ?? "en", ct));
        config.MapGet("/{model}/{id:guid}", async (Gamma.Erp.Services.Configuration.ConfigurationService s, string model, Guid id, CancellationToken ct) =>
            await s.RowAsync(model, id, ct) is { } row ? Results.Ok(row) : Results.NotFound());
        config.MapPost("/{model}", (Gamma.Erp.Services.Configuration.ConfigurationService s, string model, System.Text.Json.Nodes.JsonObject row, CancellationToken ct) =>
            s.SaveAsync(model, row, ct));
        config.MapDelete("/{model}/{id:guid}", async (Gamma.Erp.Services.Configuration.ConfigurationService s, string model, Guid id, CancellationToken ct) =>
            await s.DeleteAsync(model, id, ct) ? Results.NoContent() : Results.NotFound());
        documents.MapGet("/types/{id:guid}/settings", async (Gamma.Erp.Services.Configuration.ConfigurationService s, Guid id, CancellationToken ct) =>
            await s.DocumentTypeAsync(id, ct) is { } settings ? Results.Ok(settings) : Results.NotFound());
        documents.MapGet("/types/{id:guid}/users", (Gamma.Erp.Services.Configuration.ConfigurationService s, Guid id, CancellationToken ct) => s.DocumentTypeUsersAsync(id, ct));
        documents.MapPut("/types/{id:guid}/settings", (Gamma.Erp.Services.Configuration.ConfigurationService s, Guid id,
            Gamma.Erp.Services.Configuration.DocumentTypeSettings settings, CancellationToken ct) => s.SetDocumentTypeAsync(id, settings, ct));

        // Who may do what: rights per user or role, where a user works, passwords.
        // The whole menu and what a user or role was given of it: for administrators only.
        api.MapGet("/users/{id:guid}/menu", (MenuRightsService s, Guid id, string? language, CancellationToken ct) => s.RightsAsync(id, null, language ?? "en", ct)).WithTags("access");
        api.MapPut("/users/{id:guid}/menu", async (MenuRightsService s, Guid id, Guid[] menu, CancellationToken ct) =>
        {
            await s.SetAsync(id, null, menu, ct);
            return Results.NoContent();
        }).WithTags("access");
        api.MapGet("/roles/{id:guid}/menu", (MenuRightsService s, Guid id, string? language, CancellationToken ct) => s.RightsAsync(null, id, language ?? "en", ct)).WithTags("access");
        api.MapPut("/roles/{id:guid}/menu", async (MenuRightsService s, Guid id, Guid[] menu, CancellationToken ct) =>
        {
            await s.SetAsync(null, id, menu, ct);
            return Results.NoContent();
        }).WithTags("access");
        api.MapGet("/users/{id:guid}/rights", (Gamma.Erp.Api.Auth.AccessService s, Guid id, CancellationToken ct) => s.RightsAsync(id, null, ct)).WithTags("access");
        api.MapPut("/users/{id:guid}/rights", (Gamma.Erp.Api.Auth.AccessService s, Guid id, Gamma.Erp.Api.Auth.RightDto[] rights, CancellationToken ct) =>
            s.SetRightsAsync(id, null, rights, ct)).WithTags("access");
        api.MapGet("/roles/{id:guid}/rights", (Gamma.Erp.Api.Auth.AccessService s, Guid id, CancellationToken ct) => s.RightsAsync(null, id, ct)).WithTags("access");
        api.MapPut("/roles/{id:guid}/rights", (Gamma.Erp.Api.Auth.AccessService s, Guid id, Gamma.Erp.Api.Auth.RightDto[] rights, CancellationToken ct) =>
            s.SetRightsAsync(null, id, rights, ct)).WithTags("access");
        api.MapGet("/users/{id:guid}/cost-centers", (Gamma.Erp.Api.Auth.AccessService s, Guid id, CancellationToken ct) => s.CostCentersAsync(id, ct)).WithTags("access");
        api.MapPut("/users/{id:guid}/cost-centers", (Gamma.Erp.Api.Auth.AccessService s, Guid id, Guid[] costCenters, CancellationToken ct) =>
            s.SetCostCentersAsync(id, costCenters, ct)).WithTags("access");
        api.MapPost("/account/password", async (Gamma.Erp.Api.Auth.AccessService s, Gamma.Erp.Api.Auth.PasswordChange change, CancellationToken ct) =>
        {
            await s.ChangePasswordAsync(change, ct);
            return Results.NoContent();
        }).WithTags("access");
        api.MapPut("/account/language", async (Gamma.Erp.Api.Auth.AccessService s, Gamma.Erp.Api.Auth.LanguageChoice choice, CancellationToken ct) =>
        {
            await s.SetLanguageAsync(choice, ct);
            return Results.NoContent();
        }).WithTags("access");
        api.MapGet("/account/rights", (Gamma.Erp.Api.Auth.AccessService s, CancellationToken ct) => s.MyRightsAsync(ct)).WithTags("access");
        api.MapPut("/users/{id:guid}/username", async (Gamma.Erp.Api.Auth.AccessService s, Guid id, Gamma.Erp.Api.Auth.UsernameChange change, CancellationToken ct) =>
        {
            await s.SetUsernameAsync(id, change, ct);
            return Results.NoContent();
        }).WithTags("access");
        api.MapGet("/users/{id:guid}/email", (Gamma.Erp.Api.Auth.AccessService s, Guid id, CancellationToken ct) => s.EmailAsync(id, ct)).WithTags("access");
        api.MapGet("/users/{id:guid}/sign-in", (Gamma.Erp.Api.Auth.AccessService s, Guid id, CancellationToken ct) => s.SignInAsync(id, ct)).WithTags("access");
        api.MapPut("/users/{id:guid}/email", async (Gamma.Erp.Api.Auth.AccessService s, Guid id, Gamma.Erp.Api.Auth.UserEmail change, CancellationToken ct) =>
        {
            await s.SetEmailAsync(id, change, ct);
            return Results.NoContent();
        }).WithTags("access");
        api.MapPost("/users/{id:guid}/password", async (Gamma.Erp.Api.Auth.AccessService s, Guid id, Gamma.Erp.Api.Auth.PasswordReset reset, CancellationToken ct) =>
        {
            await s.ResetPasswordAsync(id, reset, ct);
            return Results.NoContent();
        }).WithTags("access");

        // The pipeline: what a form offers, the board, moving by hand, the forecast.
        var pipeline = api.MapGroup("/opportunities").WithTags("opportunities");
        pipeline.MapGet("/form", (Gamma.Erp.Services.Opportunities.OpportunityService s, string? language, CancellationToken ct) => s.FormAsync(language ?? "en", ct));
        pipeline.MapGet("/board", (Gamma.Erp.Services.Opportunities.OpportunityService s, string? language, CancellationToken ct) => s.BoardAsync(language ?? "en", ct)).RequireScreen("opportunity-board");
        pipeline.MapPut("/{id:guid}/stage", async (Gamma.Erp.Services.Opportunities.OpportunityService s, Guid id, OpportunityMove move, CancellationToken ct) =>
        {
            await s.MoveAsync(id, move.StageId, move.LostReason, ct);
            return Results.NoContent();
        }).RequireScreen("opportunity-board");
        pipeline.MapGet("/{id:guid}/documents", async (Gamma.Erp.Services.Opportunities.OpportunityService s, DocumentService documents, Guid id, string? language, CancellationToken ct) =>
            await s.SeesAsync(id, ct) ? Results.Ok(await documents.OpportunityDocumentsAsync(id, language ?? "en", ct)) : Results.NotFound());
        pipeline.MapGet("/documents-report", (Gamma.Erp.Services.Opportunities.OpportunityService s, DateOnly? from, DateOnly? to, Guid? stage, string? language, CancellationToken ct) =>
            s.DocumentsReportAsync(from, to, stage, language ?? "en", ct)).RequireReport("opportunity-documents");
        pipeline.MapGet("/documents-report/xlsx", async (Gamma.Erp.Services.Opportunities.OpportunityService s, DateOnly? from, DateOnly? to, Guid? stage, string? language, CancellationToken ct) =>
            Xlsx(await s.DocumentsReportAsync(from, to, stage, language ?? "en", ct), "opportunity-documents")).RequireReport("opportunity-documents");
        pipeline.MapGet("/forecast", (Gamma.Erp.Services.Opportunities.OpportunityService s, string? language, CancellationToken ct) => s.ForecastAsync(language ?? "en", ct)).RequireReport("opportunity-forecast");
        pipeline.MapGet("/forecast/xlsx", async (Gamma.Erp.Services.Opportunities.OpportunityService s, string? language, CancellationToken ct) =>
            Xlsx(await s.ForecastAsync(language ?? "en", ct), "forecast")).RequireReport("opportunity-forecast");

        // Repairs: every step by hand.
        var repairs = api.MapGroup("/repairs").WithTags("documents").RequireScreen("repairs");
        repairs.MapGet("/", (RepairService s, string? language, CancellationToken ct) => s.BoardAsync(language ?? "en", ct));
        repairs.MapGet("/clients", (RepairService s, string search, string? language, CancellationToken ct) => s.ClientsAsync(search, language ?? "en", ct));
        repairs.MapPost("/", (RepairService s, NewRepairRequest body, CancellationToken ct) => s.NewAsync(body, ct));
        repairs.MapPost("/{id:guid}/move/{stage:guid}", async (RepairService s, Guid id, Guid stage, CancellationToken ct) =>
        {
            await s.MoveAsync(id, stage, ct);
            return Results.NoContent();
        });
        repairs.MapPost("/{id:guid}/technician", async (RepairService s, Guid id, Guid? technician, CancellationToken ct) =>
        {
            await s.AssignAsync(id, technician, ct);
            return Results.NoContent();
        });
        repairs.MapPost("/{id:guid}/offer", (RepairService s, Guid id, CancellationToken ct) => s.OfferAsync(id, ct));
        repairs.MapPost("/{id:guid}/bill", (RepairService s, Guid id, CancellationToken ct) => s.BillAsync(id, ct));
        repairs.MapPost("/{id:guid}/decide", async (RepairService s, Guid id, bool accepted, CancellationToken ct) =>
        {
            await s.DecideAsync(id, accepted, ct);
            return Results.NoContent();
        });

        // Bank statements: uploaded and decided line by line, always by hand.
        var statements = api.MapGroup("/bank-statements").WithTags("documents").RequireScreen("bank-statements");
        statements.MapGet("/", (BankStatements s, string? language, CancellationToken ct) => s.ListAsync(language ?? "en", ct));
        statements.MapGet("/banks", (BankStatements s, string? language, CancellationToken ct) => s.BanksAsync(language ?? "en", ct));
        statements.MapGet("/formats", (BankStatements s, string? language, CancellationToken ct) => s.FormatsAsync(language ?? "en", ct));
        statements.MapPost("/import", async (BankStatements s, IFormFile file, Guid bankId, Guid formatId, CancellationToken ct) =>
        {
            if (file.Length > BankStatements.MaxBytes)
            {
                throw Gamma.Erp.Services.ValidationException.For("file", "The file is too large.");
            }

            await using var content = file.OpenReadStream();
            return await s.ImportAsync(content, file.FileName, bankId, formatId, ct);
        }).DisableAntiforgery();
        statements.MapGet("/{id:guid}/lines", (BankStatements s, Guid id, CancellationToken ct) => s.LinesAsync(id, ct));
        statements.MapDelete("/{id:guid}", async (BankStatements s, Guid id, CancellationToken ct) =>
        {
            await s.RemoveAsync(id, ct);
            return Results.NoContent();
        });
        statements.MapGet("/lines/{id:guid}/choices", (BankStatements s, Guid id, string? language, CancellationToken ct) => s.ChoicesAsync(id, language ?? "en", ct));
        statements.MapPost("/lines/{id:guid}/match/{document:guid}", async (BankStatements s, Guid id, Guid document, CancellationToken ct) =>
        {
            await s.MatchAsync(id, document, ct);
            return Results.NoContent();
        });
        statements.MapPost("/lines/{id:guid}/ignore", async (BankStatements s, Guid id, CancellationToken ct) =>
        {
            await s.IgnoreAsync(id, ct);
            return Results.NoContent();
        });
        statements.MapPost("/lines/{id:guid}/clear", async (BankStatements s, Guid id, CancellationToken ct) =>
        {
            await s.ClearAsync(id, ct);
            return Results.NoContent();
        });

        // Approvals, always by hand.
        api.MapGet("/approvals", (Approvals s, string? language, CancellationToken ct) => s.WaitingAsync(language ?? "en", ct)).WithTags("documents").RequireScreen("approvals");
        api.MapPost("/approvals/{id:guid}", async (Approvals s, Guid id, CancellationToken ct) =>
        {
            await s.ApproveAsync(id, ct);
            return Results.NoContent();
        }).WithTags("documents").RequireScreen("approvals");

        // Contract billing and renewals. The plan is read and set on the contract; the run and the renewals have their own screens.
        var billing = api.MapGroup("/contract-billing").WithTags("documents");
        billing.MapGet("/due", (ContractBillingService s, DateOnly until, CancellationToken ct) => s.DueAsync(until, ct)).RequireScreen("invoice-run");
        billing.MapPost("/run", (ContractBillingService s, DateOnly until, CancellationToken ct) => s.RunAsync(until, null, ct)).RequireScreen("invoice-run");
        billing.MapGet("/renewals", (ContractBillingService s, int days, CancellationToken ct) => s.RenewalsAsync(days, ct)).RequireScreen("renewals");
        billing.MapPost("/{id:guid}/renew", (ContractBillingService s, Guid id, DateOnly endDate, CancellationToken ct) => s.RenewAsync(id, endDate, ct)).RequireScreen("renewals");
        billing.MapGet("/{id:guid}", (ContractBillingService s, Guid id, CancellationToken ct) => s.PlanAsync(id, ct)).RequireScreen("documents");
        billing.MapPut("/{id:guid}", async (ContractBillingService s, Guid id, ContractPlanRequest plan, CancellationToken ct) =>
        {
            await s.SetPlanAsync(id, plan.EveryMonths, plan.NextDate, ct);
            return Results.NoContent();
        }).RequireScreen("documents");

        // Closing a fiscal year: its state, and locking it.
        api.MapGet("/year-closing", (Gamma.Erp.Services.Reports.YearClosing s, DateOnly day, CancellationToken ct) => s.CheckAsync(day, ct)).WithTags("reports").RequireScreen("year-closing");
        api.MapPost("/year-closing", (Gamma.Erp.Services.Reports.YearClosing s, DateOnly day, Guid? nextCurrency, CancellationToken ct) => s.CloseAsync(day, ct, nextCurrency))
            .WithTags("reports").RequireScreen("year-closing");

        // What is open and how late it is.
        reports.MapGet("/ageing", (OpenItems s, DateOnly to, CancellationToken ct) => s.AgeingAsync(to, ct)).RequireReport("ageing");
        reports.MapGet("/open-items", (OpenItems s, DateOnly to, Guid? partner, CancellationToken ct) => s.ListAsync(to, partner, ct)).RequireReport("open-items");

        // Stock and sales reports.
        reports.MapGet("/stock", (Gamma.Erp.Services.Reports.StockReportService s, Guid? costCenter, Guid? productCategory, string? holder, string? language, CancellationToken ct) =>
            s.StockAsync(costCenter, productCategory, holder ?? Gamma.Erp.Services.Reports.StockReportService.PlaceHolder, language ?? "en", ct)).RequireReport("stock");
        // The dashboard: the company's configured cards and charts, for whoever has the screen in their menu.
        api.MapGet("/dashboard", (Gamma.Erp.Services.Reports.DashboardService s, DateOnly from, DateOnly to, Guid? costCenter, string? language, CancellationToken ct) =>
            s.DashboardAsync(from, to, costCenter, language ?? "en", ct)).WithTags("reports");
        // The Migration menu: each step's export, template and import. An import without confirm only checks.
        var migration = api.MapGroup("/migration").WithTags("migration");
        migration.MapGet("/", (Gamma.Erp.Services.Migration.MigrationService s, string? language, CancellationToken ct) => s.StepsAsync(language ?? "en", ct));
        migration.MapGet("/opening", (Gamma.Erp.Services.Migration.MigrationService s, string? language, CancellationToken ct) => s.OpeningAsync(language ?? "en", ct));
        migration.MapGet("/{code}/export", async (Gamma.Erp.Services.Migration.MigrationService s, string code, CancellationToken ct) =>
            Results.File(await s.ExportAsync(code, ct), Gamma.Erp.Services.Migration.MigrationService.ContentType, $"{code}.xlsx"));
        migration.MapGet("/{code}/template", async (Gamma.Erp.Services.Migration.MigrationService s, string code, CancellationToken ct) =>
            Results.File(await s.TemplateAsync(code, ct), Gamma.Erp.Services.Migration.MigrationService.ContentType, $"{code}-template.xlsx"));
        migration.MapPost("/{code}/import", async (Gamma.Erp.Services.Migration.MigrationService s, string code, IFormFile file, bool? confirm, CancellationToken ct) =>
        {
            if (file.Length > Gamma.Erp.Services.Migration.MigrationService.MaxBytes)
            {
                throw Gamma.Erp.Services.ValidationException.For("file", "The file is too large.");
            }

            await using var content = file.OpenReadStream();
            return await s.ImportAsync(code, content, file.FileName, confirm ?? false, ct);
        }).DisableAntiforgery();

        // Who did what: for whoever has the User activity screen in their menu.
        var audit = api.MapGroup("/audit").WithTags("audit");
        audit.MapGet("/users", (Gamma.Erp.Services.Audit.AuditService s, CancellationToken ct) => s.UsersAsync(ct));
        audit.MapGet("/sign-ins", (Gamma.Erp.Services.Audit.AuditService s, DateOnly from, DateOnly to, Guid? user, CancellationToken ct) => s.SignInsAsync(from, to, user, ct));
        audit.MapGet("/activity", (Gamma.Erp.Services.Audit.AuditService s, DateOnly from, DateOnly to, Guid? user, CancellationToken ct) => s.ActivityAsync(from, to, user, ct));
        audit.MapGet("/activity/{table}", (Gamma.Erp.Services.Audit.AuditService s, string table, DateOnly from, DateOnly to, Guid? user, string? language, CancellationToken ct) =>
            s.RowsAsync(table, from, to, user, language ?? "en", ct));
        audit.MapGet("/activity/{table}/{id:guid}", (Gamma.Erp.Services.Audit.AuditService s, string table, Guid id, string? language, CancellationToken ct) => s.ChangeAsync(table, id, language ?? "en", ct));
        // Lots. A point of sale reads what it needs without the Lots screen.
        var lots = api.MapGroup("/lots").WithTags("serials");
        lots.MapGet("/", (Gamma.Erp.Services.Stock.LotService s, string? search, DateOnly? expiresBy, string? language, CancellationToken ct) => s.ListAsync(search, expiresBy, language ?? "en", ct)).RequireScreen("lots");
        lots.MapGet("/stock", (Gamma.Erp.Services.Stock.LotService s, string? language, CancellationToken ct) => s.StockAsync(language ?? "en", ct)).RequireScreen("lots");
        lots.MapPost("/assign", async (Gamma.Erp.Services.Stock.LotService s, Gamma.Erp.Services.Stock.AssignLotRequest body, CancellationToken ct) =>
        {
            await s.AssignAsync(body, ct);
            return Results.NoContent();
        }).RequireScreen("lots");
        lots.MapGet("/held", (Gamma.Erp.Services.Stock.LotService s, Guid product, Guid holder, Guid? unit, CancellationToken ct) => s.HeldAsync(product, holder, unit, ct));
        lots.MapGet("/tracked", (Gamma.Erp.Services.Stock.LotService s, CancellationToken ct) => s.TrackedAsync(ct));

        var serials = api.MapGroup("/serials").WithTags("serials");
        serials.MapGet("/", (Gamma.Erp.Services.Stock.SerialService s, Guid? product, Guid? holder, string? search, string? language, CancellationToken ct) => s.SerialsAsync(product, holder, search, language ?? "en", ct));
        serials.MapGet("/stock", (Gamma.Erp.Services.Stock.SerialService s, string? language, CancellationToken ct) => s.StockAsync(language ?? "en", ct)).RequireScreen("serials");
        serials.MapGet("/{id:guid}/moves", (Gamma.Erp.Services.Stock.SerialService s, Guid id, string? language, CancellationToken ct) => s.MovesAsync(id, language ?? "en", ct)).RequireScreen("serials");
        serials.MapPost("/assign", (Gamma.Erp.Services.Stock.SerialService s, Gamma.Erp.Services.Stock.AssignSerialsRequest body, string? language, CancellationToken ct) => s.AssignAsync(body, language ?? "en", ct)).RequireScreen("serials");
        reports.MapGet("/purchase-prices", (Gamma.Erp.Services.Reports.StockReportService s, Guid product, DateOnly from, DateOnly to, CancellationToken ct) =>
            s.PurchasePricesAsync(product, from, to, ct)).RequireReport("purchase-prices");
        reports.MapGet("/item-tracking", (Gamma.Erp.Services.Reports.StockReportService s, Guid product, DateOnly from, DateOnly to, string? language, CancellationToken ct) =>
            s.ItemTrackingAsync(product, from, to, language ?? "en", ct)).RequireReport("item-tracking");
        reports.MapGet("/lines", (Gamma.Erp.Services.Reports.StockReportService s, Guid[] category, DateOnly from, DateOnly to, Guid? costCenter, Guid? partner, string? language,
            CancellationToken ct) => s.LinesAsync(category, from, to, costCenter, partner, language ?? "en", ct)).RequireReport("lines", "lines-by-partner", "lines-by-cost-center", "lines-by-user", "transfers");

        // Every list and report as an Excel sheet.
        reports.MapGet("/trial-balance/xlsx", async (Gamma.Erp.Services.Reports.ReportService s, DateOnly from, DateOnly to, Guid? chart, string? language, Guid? costCenter,
            CancellationToken ct) => Xlsx((await s.TrialBalanceAsync(from, to, chart, language ?? "en", costCenter, ct)).Rows, "trial-balance")).RequireReport("trial-balance");
        reports.MapGet("/ledger/xlsx", async (Gamma.Erp.Services.Reports.ReportService s, Guid account, DateOnly from, DateOnly to, string? language, Guid? costCenter,
            CancellationToken ct) => await s.LedgerAsync(account, from, to, language ?? "en", costCenter, ct) is { } l ? Xlsx(l.Lines, "ledger") : Results.NotFound()).RequireReport("ledger");
        reports.MapGet("/vat-register/xlsx", async (Gamma.Erp.Services.Reports.ReportService s, Guid[] category, DateOnly from, DateOnly to, string? language,
            CancellationToken ct) => Xlsx((await s.VatRegisterAsync(category, from, to, language ?? "en", ct)).Lines, "vat-register")).RequireReport("vat-register");
        reports.MapGet("/stock/xlsx", async (Gamma.Erp.Services.Reports.StockReportService s, Guid? costCenter, Guid? productCategory, string? holder, string? language, CancellationToken ct) =>
            Xlsx(await s.StockAsync(costCenter, productCategory, holder ?? Gamma.Erp.Services.Reports.StockReportService.PlaceHolder, language ?? "en", ct), "stock")).RequireReport("stock");
        reports.MapGet("/item-tracking/xlsx", async (Gamma.Erp.Services.Reports.StockReportService s, Guid product, DateOnly from, DateOnly to, string? language,
            CancellationToken ct) => Xlsx(await s.ItemTrackingAsync(product, from, to, language ?? "en", ct), "item-tracking")).RequireReport("item-tracking");
        reports.MapGet("/lines/xlsx", async (Gamma.Erp.Services.Reports.StockReportService s, Guid[] category, DateOnly from, DateOnly to, Guid? costCenter, Guid? partner,
            string? language, CancellationToken ct) => Xlsx((await s.LinesAsync(category, from, to, costCenter, partner, language ?? "en", ct)).Lines, "lines")).RequireReport("lines", "lines-by-partner", "lines-by-cost-center", "lines-by-user", "transfers");
        // B8 (C3 phase 6): the VAT return as a sheet, output then input, one row per VAT group.
        reports.MapGet("/vat-return/xlsx", async (Gamma.Erp.Services.Reports.ReportService s, Guid[] output, Guid[] input, DateOnly from, DateOnly to, string? language,
            CancellationToken ct) =>
        {
            var vat = await s.VatReturnAsync(output, input, from, to, language ?? "en", ct);
            return Xlsx(vat.Output.Select(v => new VatReturnRow("Output", v.VatGroup, v.Rate, v.Base, v.Vat))
                .Concat(vat.Input.Select(v => new VatReturnRow("Input", v.VatGroup, v.Rate, v.Base, v.Vat))), "vat-return");
        }).RequireReport("vat-return");
        // B9 (C3 phase 6): the stock reports as PDF.
        reports.MapGet("/stock/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid? costCenter, Guid? productCategory, string? holder, string? language, CancellationToken ct) =>
            Pdf(await s.StockAsync(costCenter, productCategory, holder ?? Gamma.Erp.Services.Reports.StockReportService.PlaceHolder, language ?? "en", ct))).RequireReport("stock");
        reports.MapGet("/item-tracking/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid product, DateOnly from, DateOnly to, string? language,
            CancellationToken ct) => Pdf(await s.ItemTrackingAsync(product, from, to, language ?? "en", ct))).RequireReport("item-tracking");
        reports.MapGet("/lines/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid[] category, DateOnly from, DateOnly to, Guid? costCenter, Guid? partner,
            string? language, CancellationToken ct) => Pdf(await s.LinesAsync(category, from, to, costCenter, partner, language ?? "en", ct))).RequireReport("lines", "lines-by-partner", "lines-by-cost-center", "lines-by-user", "transfers");
        documents.MapGet("/xlsx", async (DocumentService s, [AsParameters] DocumentListParameters p, CancellationToken ct) =>
        {
            // The whole list, page by page (500 a page), up to 50 000 rows.
            var rows = new List<DocumentListItemDto>();
            for (var page = 1; page <= 100; page++)
            {
                var part = await s.ListAsync(p.Query() with { Page = page, PageSize = 500 }, ct);
                rows.AddRange(part.Items);
                if (rows.Count >= part.Total || part.Items.Count == 0)
                {
                    break;
                }
            }

            return Xlsx(rows, "documents");
        });

        // A product's units and a product type's (the founder, 2026-10-31): the list in order, the first the default.
        api.MapGet("/products/{id:guid}/units", async (Gamma.Erp.Services.Stock.ProductUnitService s, Guid id, string? language, CancellationToken ct) =>
            await s.ProductAsync(id, language ?? "en", ct) is { } units ? Results.Ok(units) : Results.NotFound()).WithTags("products");
        api.MapPut("/products/{id:guid}/units", (Gamma.Erp.Services.Stock.ProductUnitService s, Guid id, Guid[] unitIds, string? language, CancellationToken ct) =>
            s.SaveProductAsync(id, unitIds, language ?? "en", ct)).WithTags("products");
        api.MapGet("/products/types/{id:guid}/units", async (Gamma.Erp.Services.Stock.ProductUnitService s, Guid id, string? language, CancellationToken ct) =>
            await s.TypeAsync(id, language ?? "en", ct) is { } units ? Results.Ok(units) : Results.NotFound()).WithTags("products");
        api.MapPut("/products/types/{id:guid}/units", (Gamma.Erp.Services.Stock.ProductUnitService s, Guid id, Guid[] unitIds, string? language, CancellationToken ct) =>
            s.SaveTypeAsync(id, unitIds, language ?? "en", ct)).WithTags("products");

        // The point of sale: the menus open now, the bills not yet paid, paying them.
        var pos = api.MapGroup("/pos").WithTags("pos").RequireScreen("pos");
        pos.MapGet("/menus/current", (Gamma.Erp.Services.Pos.PosService s, Guid? pointOfSale, TimeOnly? at, string? language, CancellationToken ct) =>
            s.CurrentMenusAsync(pointOfSale, at, language ?? "en", ct));
        pos.MapGet("/shows", (Gamma.Erp.Services.Pos.ReservationService s, Guid pointOfSale, DateOnly? from, string? language, CancellationToken ct) =>
            s.ShowsAsync(pointOfSale, from, language ?? "en", ct));
        pos.MapGet("/shows/{id:guid}/taken", (Gamma.Erp.Services.Pos.ReservationService s, Guid id, CancellationToken ct) => s.TakenAsync(id, ct));
        pos.MapPost("/shows/book", (Gamma.Erp.Services.Pos.ReservationService s, Guid pointOfSale, Gamma.Erp.Services.Pos.BookSeatsRequest book, string? language, CancellationToken ct) =>
            s.BookSeatsAsync(pointOfSale, book, language ?? "en", ct));
        pos.MapPost("/shows/cancel/{bill:guid}", async (Gamma.Erp.Services.Pos.ReservationService s, Guid bill, CancellationToken ct) =>
        {
            await s.CancelSeatsAsync(bill, ct);
            return Results.NoContent();
        });
        pos.MapGet("/stays", (Gamma.Erp.Services.Pos.ReservationService s, Guid pointOfSale, DateOnly from, DateOnly to, CancellationToken ct) => s.StaysAsync(pointOfSale, from, to, ct));
        pos.MapPost("/stays", (Gamma.Erp.Services.Pos.ReservationService s, Guid pointOfSale, Gamma.Erp.Services.Pos.BookStayRequest book, CancellationToken ct) =>
            s.BookStayAsync(pointOfSale, book, ct));
        pos.MapPost("/stays/{id:guid}/check-in", (Gamma.Erp.Services.Pos.ReservationService s, Guid id, CancellationToken ct) => s.CheckInAsync(id, ct));
        pos.MapPost("/stays/{id:guid}/extend", (Gamma.Erp.Services.Pos.ReservationService s, Guid id, DateOnly toDate, CancellationToken ct) => s.ExtendStayAsync(id, toDate, ct));
        pos.MapPost("/stays/{id:guid}/check-out", (Gamma.Erp.Services.Pos.ReservationService s, Guid id, string? language, CancellationToken ct) => s.CheckOutAsync(id, language ?? "en", ct));
        pos.MapDelete("/stays/{id:guid}", async (Gamma.Erp.Services.Pos.ReservationService s, Guid id, CancellationToken ct) =>
        {
            await s.CancelStayAsync(id, ct);
            return Results.NoContent();
        });
        // The point of sale a place works as (a till works as its shop): null when it is none.
        api.MapGet("/pos/point-of-sale-of", async (Gamma.Erp.Persistence.ErpDbContext db, Guid place, CancellationToken ct) =>
            Results.Ok(new { pointOfSaleId = await Gamma.Erp.Services.Pos.PosPlaces.PointOfSaleOfAsync(db, place, ct) })).WithTags("pos");
        pos.MapGet("/barcodes", (Gamma.Erp.Services.Pos.PosService s, string? language, CancellationToken ct) => s.BarcodesAsync(language ?? "en", ct));
        // A point of sale's settings and places: its register's setup reads them too, so not only the point of sale's own screen.
        // The production point display: produced is said by hand.
        var producing = api.MapGroup("/production-display").WithTags("pos").RequireScreen("production-display");
        producing.MapGet("/points", (Gamma.Erp.Services.Pos.ProductionDisplay s, string? language, CancellationToken ct) => s.PointsAsync(language ?? "en", ct));
        producing.MapGet("/not-routed", (Gamma.Erp.Services.Pos.ProductionDisplay s, CancellationToken ct) => s.NotRoutedAsync(ct));
        producing.MapGet("/{point:guid}", (Gamma.Erp.Services.Pos.ProductionDisplay s, Guid point, string? language, CancellationToken ct) => s.BoardAsync(point, language ?? "en", ct));
        producing.MapPost("/{point:guid}/produced", async (Gamma.Erp.Services.Pos.ProductionDisplay s, Guid point, Gamma.Erp.Services.Pos.ProductionLinesRequest body, CancellationToken ct) =>
        {
            await s.ProducedAsync(point, body.LineIds, ct);
            return Results.NoContent();
        });
        producing.MapPost("/{point:guid}/undo", async (Gamma.Erp.Services.Pos.ProductionDisplay s, Guid point, Gamma.Erp.Services.Pos.ProductionLinesRequest body, CancellationToken ct) =>
        {
            await s.UndoAsync(point, body.LineIds, ct);
            return Results.NoContent();
        });

        // Orders: sent by a waiter, billed at payment.
        pos.MapGet("/orders/open", (Gamma.Erp.Services.Pos.PosOrders s, Guid type, Guid? table, CancellationToken ct) => s.OpenAsync(type, table, ct));
        pos.MapPost("/orders/bill", (Gamma.Erp.Services.Pos.PosOrders s, Gamma.Erp.Services.Pos.BillOrdersRequest body, CancellationToken ct) => s.BillAsync(body, ct));
        pos.MapDelete("/orders/{id:guid}", async (Gamma.Erp.Services.Pos.PosOrders s, Guid id, CancellationToken ct) =>
        {
            await s.CancelAsync(id, ct);
            return Results.NoContent();
        });

        // Shifts and cash-up, by hand.
        pos.MapGet("/shift/current", (Gamma.Erp.Services.Pos.PosShifts s, Guid pointOfSale, string? language, CancellationToken ct) => s.CurrentAsync(pointOfSale, language ?? "en", ct));
        pos.MapPost("/shift/open", (Gamma.Erp.Services.Pos.PosShifts s, Gamma.Erp.Services.Pos.OpenShiftRequest body, string? language, CancellationToken ct) => s.OpenAsync(body, language ?? "en", ct));
        pos.MapGet("/shift/{id:guid}", (Gamma.Erp.Services.Pos.PosShifts s, Guid id, string? language, CancellationToken ct) => s.SummaryAsync(id, language ?? "en", ct));
        pos.MapPost("/shift/{id:guid}/close", (Gamma.Erp.Services.Pos.PosShifts s, Guid id, Gamma.Erp.Services.Pos.CloseShiftRequest body, string? language, CancellationToken ct) => s.CloseAsync(id, body, language ?? "en", ct));
        api.MapGet("/pos-shifts", (Gamma.Erp.Services.Pos.PosShifts s, string? language, CancellationToken ct) => s.ListAsync(language ?? "en", ct)).WithTags("pos").RequireScreen("pos-shifts");
        api.MapGet("/pos-shifts/{id:guid}", (Gamma.Erp.Services.Pos.PosShifts s, Guid id, string? language, CancellationToken ct) => s.SummaryAsync(id, language ?? "en", ct)).WithTags("pos").RequireScreen("pos-shifts");

        var posSetup = api.MapGroup("/pos").WithTags("pos");
        posSetup.MapGet("/settings", (Gamma.Erp.Services.Pos.PosService s, Guid? pointOfSale, string? language, CancellationToken ct) =>
            s.SettingsAsync(pointOfSale, language ?? "en", ct));
        posSetup.MapGet("/places", (Gamma.Erp.Services.Pos.PosService s, Guid pointOfSale, string? language, CancellationToken ct) =>
            s.PlacesAsync(pointOfSale, language ?? "en", ct));
        pos.MapGet("/payment-methods", (Gamma.Erp.Services.Pos.PosService s, Guid costCenter, CancellationToken ct) => s.MethodsAtAsync(costCenter, ct));
        pos.MapGet("/bills/unpaid", (Gamma.Erp.Services.Pos.PosService s, Guid type, Guid? user, Guid? table, Guid? pointOfSale, CancellationToken ct) =>
            s.UnpaidAsync(type, user, table, ct, pointOfSale));
        var stock = api.MapGroup("/stock").WithTags("stock").RequireScreen("stock-convert");
        stock.MapPost("/convert", (Gamma.Erp.Services.Stock.UnitConversionService s, Gamma.Erp.Services.Stock.ConvertStockRequest convert, CancellationToken ct) => s.ConvertAsync(convert, ct));
        stock.MapGet("/convert/types", (Gamma.Erp.Services.Stock.ProductionService s, CancellationToken ct) => s.TypesOfAsync("unit-split", ct));
        stock.MapGet("/factor", async (Gamma.Erp.Services.Stock.UnitConversionService s, Guid from, Guid to, Guid? product, CancellationToken ct) =>
            await s.FactorAsync(from, to, product, ct) is { } f ? Results.Ok(f) : Results.NotFound());
        var production = api.MapGroup("/production").WithTags("production").RequireScreen("production");
        production.MapGet("/calculate", (Gamma.Erp.Services.Stock.ProductionService s, Guid product, Guid unit, decimal quantity, Guid? costCenter, CancellationToken ct) =>
            s.CalculateAsync(product, unit, quantity, costCenter, ct));
        production.MapPost("/", (Gamma.Erp.Services.Stock.ProductionService s, Gamma.Erp.Services.Stock.ProduceRequest produce, CancellationToken ct) => s.ProduceAsync(produce, ct));
        production.MapGet("/orders", (Gamma.Erp.Services.Stock.ProductionService s, Guid type, CancellationToken ct) => s.OrdersToProduceAsync(type, ct));
        production.MapGet("/order-types", (Gamma.Erp.Services.Stock.ProductionService s, CancellationToken ct) => s.OrderTypesAsync(ct));
        production.MapGet("/types", (Gamma.Erp.Services.Stock.ProductionService s, string kind, CancellationToken ct) => s.TypesOfAsync(kind, ct));
        production.MapGet("/held", (Gamma.Erp.Services.Stock.ProductionService s, Guid costCenter, Guid product, Guid unit, CancellationToken ct) => s.HeldAsync(costCenter, product, unit, ct));
        // Disassembly: a product taken apart by its recipe read backwards; what is not recovered is waste.
        production.MapGet("/disassemble/calculate", (Gamma.Erp.Services.Stock.ProductionService s, Guid product, Guid unit, decimal quantity, Guid? costCenter,
            CancellationToken ct) => s.CalculateDisassemblyAsync(product, unit, quantity, costCenter, ct));
        production.MapPost("/disassemble", (Gamma.Erp.Services.Stock.ProductionService s, Gamma.Erp.Services.Stock.DisassembleRequest take, CancellationToken ct) =>
            s.DisassembleAsync(take, ct));
        production.MapPost("/orders/{id:guid}", (Gamma.Erp.Services.Stock.ProductionService s, Guid id, Guid costCenter, DateOnly? date, CancellationToken ct) =>
            s.ProduceOrderAsync(id, costCenter, date, ct));
        pos.MapPost("/bills/pay", (Gamma.Erp.Services.Pos.PosService s, Gamma.Erp.Services.Pos.PayBillsRequest pay, CancellationToken ct) => s.PayAsync(pay, ct));

        // Currencies and their rates by day.
        api.MapGet("/currencies", (Gamma.Erp.Services.Currencies.CurrencyService s, string? language, CancellationToken ct) => s.CurrenciesAsync(language ?? "en", ct))
            .WithTags("currencies").RequireScreen("currencies");
        api.MapPost("/currencies", (Gamma.Erp.Services.Currencies.CurrencyService s, Gamma.Erp.Services.Currencies.SaveCurrencyRequest currency, CancellationToken ct) =>
            s.SaveCurrencyAsync(currency, ct)).WithTags("currencies").RequireScreen("currencies").RequireAdministrator();
        api.MapDelete("/currencies/{id:guid}", async (Gamma.Erp.Services.Currencies.CurrencyService s, Guid id, CancellationToken ct) =>
        {
            await s.SwitchOffCurrencyAsync(id, ct);
            return Results.NoContent();
        }).WithTags("currencies").RequireScreen("currencies").RequireAdministrator();
        api.MapGet("/exchange-rates", (Gamma.Erp.Services.Currencies.CurrencyService s, Guid currency, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            s.RatesAsync(currency, from, to, ct)).WithTags("currencies").RequireScreen("currencies");
        api.MapPost("/exchange-rates", (Gamma.Erp.Services.Currencies.CurrencyService s, Gamma.Erp.Services.Currencies.SetRateRequest rate, CancellationToken ct) =>
            s.SetRateAsync(rate, ct)).WithTags("currencies").RequireScreen("currencies").RequireAdministrator();
        api.MapGet("/exchange-rates/rate", async (Gamma.Erp.Services.Currencies.CurrencyService s, Guid currency, DateOnly date, CancellationToken ct) =>
            Results.Ok(new { rate = await s.RateAsync(currency, date, ct) })).WithTags("currencies");

        // Files: uploaded as multipart (field "file"), read back as a download that the browser never runs or renders in the page.
        var files = api.MapGroup("/files").WithTags("files").RequireScreen("files");
        files.MapDelete("/{id:guid}", async (Gamma.Erp.Services.Files.FileService s, Guid id, CancellationToken ct) =>
        {
            await s.RemoveAsync(id, ct);
            return Results.NoContent();
        });
        files.MapPost("/", async (Gamma.Erp.Services.Files.FileService s, IFormFile file, CancellationToken ct) =>
        {
            await using var content = file.OpenReadStream();
            var stored = await s.UploadAsync(file.FileName, file.ContentType, content, ct);
            return Results.Created(stored.Link, stored);
        }).DisableAntiforgery();
        files.MapGet("/", (Gamma.Erp.Services.Files.FileService s, string? search, int? page, int? pageSize, CancellationToken ct) =>
            s.ListAsync(search, page ?? 1, pageSize ?? 50, ct));
        files.MapGet("/{id:guid}/info", async (Gamma.Erp.Services.Files.FileService s, Guid id, CancellationToken ct) =>
            await s.FileAsync(id, ct) is { } f ? Results.Ok(f) : Results.NotFound());
        files.MapGet("/{id:guid}", async (HttpContext http, Gamma.Erp.Services.Files.FileService s, Guid id, CancellationToken ct) =>
        {
            if (await s.ReadAsync(id, ct) is not { } f)
            {
                return Results.NotFound();
            }

            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return Results.File(f.Content, f.File.ContentType, f.File.FileName);
        });

        var depreciation = api.MapGroup("/depreciation").WithTags("depreciation");
        depreciation.MapGet("/books", (DepreciationService s, string? language, CancellationToken ct) => s.BooksAsync(language ?? "en", ct)).RequireScreen("depreciation", "asset-value");
        depreciation.MapPost("/assets", (DepreciationService s, AssetRegistration asset, CancellationToken ct) => s.RegisterAsync(asset, ct)).RequireScreen("depreciation");
        depreciation.MapGet("/asset-products", (DepreciationService s, string? language, CancellationToken ct) => s.AssetProductsAsync(language ?? "en", ct))
            .RequireScreen("depreciation", "asset-value");
        depreciation.MapGet("/books/{book:guid}/calculate", (DepreciationService s, Guid book, DateOnly date, CancellationToken ct) => s.CalculateAsync(book, date, ct)).RequireScreen("depreciation");
        depreciation.MapPost("/books/{book:guid}/runs", (DepreciationService s, Guid book, DateOnly date, CancellationToken ct) => s.RunAsync(book, date, ct)).RequireScreen("depreciation");
        depreciation.MapPost("/books/{book:guid}/journals", (DepreciationService s, Guid book, JournalRequest body, CancellationToken ct) =>
            s.JournalAsync(book, body.Date, body.DocumentTypeId, body.CategoryId, ct)).RequireScreen("depreciation");
        depreciation.MapGet("/books/{book:guid}/assets", (DepreciationService s, Guid book, string? language, CancellationToken ct) => s.AssetsAsync(book, language ?? "en", ct)).RequireScreen("depreciation");
        depreciation.MapPost("/assets/{id:guid}/disposed", async (DepreciationService s, Guid id, bool disposed, CancellationToken ct) =>
        {
            await s.SetDisposedAsync(id, disposed, ct);
            return Results.NoContent();
        }).RequireScreen("depreciation");
        depreciation.MapGet("/assets/{id:guid}/history", (DepreciationService s, Guid id, CancellationToken ct) => s.AssetHistoryAsync(id, ct)).RequireScreen("depreciation");
        depreciation.MapGet("/books/{book:guid}/runs", (DepreciationService s, Guid book, CancellationToken ct) => s.RunsAsync(book, ct)).RequireScreen("depreciation");
        depreciation.MapGet("/books/{book:guid}/runs/{date}", (DepreciationService s, Guid book, DateOnly date, string? language, CancellationToken ct) => s.RunLinesAsync(book, date, language ?? "en", ct)).RequireScreen("depreciation");
        depreciation.MapDelete("/books/{book:guid}/runs/{date}", async (DepreciationService s, Guid book, DateOnly date, CancellationToken ct) =>
        {
            await s.UndoRunAsync(book, date, ct);
            return Results.NoContent();
        }).RequireScreen("depreciation");
        depreciation.MapGet("/remaining", async (DepreciationService s, Guid product, Guid unit, Guid book, CancellationToken ct) =>
            Results.Ok(new { remaining = await s.RemainingValueAsync(product, unit, book, ct) })).RequireScreen("asset-value");

        // CRM statistics per partner.
        api.MapGet("/partners/{id:guid}/statistics", async (PartnerStatisticsService s, Guid id, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            await s.StatisticsAsync(id, from, to, ct) is { } f ? Results.Ok(f) : Results.NotFound()).WithTags("statistics")
            // The partner register, the Client statistics report, or writing documents (the editor shows the partner's balance from here).
            .AddEndpointFilter(async (context, next) =>
            {
                var visibility = context.HttpContext.RequestServices.GetRequiredService<Visibility>();
                var ct = context.HttpContext.RequestAborted;
                if (!await visibility.OpensScreenAsync(["entities", "report"], ["partners", "partner-statistics"], ct) && !await visibility.OpensScreenAsync("documents", ct))
                {
                    throw Gamma.Erp.Services.ValidationException.For("rights", $"{Gamma.Erp.Services.ErrorCodes.NotAllowedError}: this screen is not in your menu.");
                }

                return await next(context);
            });
        api.MapGet("/partners/{id:guid}/ledger", (PartnerStatisticsService s, Guid id, DateOnly from, DateOnly to, CancellationToken ct) => s.LedgerAsync(id, from, to, ct))
            .WithTags("statistics").RequireReport("partner-ledger");
        api.MapGet("/statistics/partners", (PartnerStatisticsService s, Guid category, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct) =>
            s.RankingAsync(category, from, to, page ?? 1, pageSize ?? 50, ct)).WithTags("statistics").RequireReport("partner-statistics");
        // B8 / B9 (C3 phase 6): the partner ledger and the partners' ranking as Excel and PDF.
        api.MapGet("/partners/{id:guid}/ledger/xlsx", async (PartnerStatisticsService s, Guid id, DateOnly from, DateOnly to, CancellationToken ct) =>
            Xlsx(await s.LedgerAsync(id, from, to, ct), "partner-ledger")).WithTags("statistics").RequireReport("partner-ledger");
        api.MapGet("/partners/{id:guid}/ledger/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid id, DateOnly from, DateOnly to, string? language,
            CancellationToken ct) => await s.PartnerLedgerAsync(id, from, to, language ?? "en", ct) is { } pdf ? Pdf(pdf) : Results.NotFound()).WithTags("statistics").RequireReport("partner-ledger");
        api.MapGet("/statistics/partners/xlsx", async (PartnerStatisticsService s, Guid category, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            Xlsx((await s.RankingAsync(category, from, to, 1, 500, ct)).Items, "partners")).WithTags("statistics").RequireReport("partner-statistics");
        api.MapGet("/statistics/partners/pdf", async (Gamma.Erp.Services.Printing.PrintService s, Guid category, DateOnly? from, DateOnly? to, string? language,
            CancellationToken ct) => Pdf(await s.PartnerRankingAsync(category, from, to, language ?? "en", ct))).WithTags("statistics").RequireReport("partner-statistics");
    }

    /// <summary>One entity's routes (its nine tables) through the generic entity service.</summary>
    /// <summary>The day a document is done again for, and whether to post it.</summary>
    private sealed record RepeatRequest(DateOnly Date, bool Post = false);

    /// <summary>The query string of a document list.</summary>
    private sealed record DocumentListParameters(Guid Category, Guid? Type, string? Search, string? Code, string? From, string? To, string? ExternalNumber, bool? Posted,
        DateOnly? PostedFrom, DateOnly? PostedTo, DateOnly? DatedFrom, DateOnly? DatedTo, Guid? CreatedBy, DateTimeOffset? CreatedFrom, DateTimeOffset? CreatedTo,
        decimal? TotalFrom, decimal? TotalTo, string? Sort, bool? Descending, int? Page, int? PageSize)
    {
        public DocumentQuery Query() => new(Category, Type, Search, Code, From, To, ExternalNumber, Posted, PostedFrom, PostedTo, DatedFrom, DatedTo, CreatedBy, CreatedFrom,
            CreatedTo, TotalFrom, TotalTo, Sort, Descending ?? false, Page ?? 1, PageSize ?? 50);
    }

    /// <summary>A row of the VAT return sheet: output or input, the VAT group, its rate, base and VAT.</summary>
    private sealed record VatReturnRow(string Side, string? VatGroup, decimal? Rate, decimal Base, decimal Vat);

    private static IResult Xlsx<T>(IEnumerable<T> rows, string name) =>
        Results.File(Gamma.Erp.Services.Printing.ExcelExport.Write(rows, name), Gamma.Erp.Services.Printing.ExcelExport.ContentType, $"{name}.xlsx");

    private static IResult Pdf(Gamma.Erp.Services.Printing.PdfFile pdf) => Results.File(pdf.Content, "application/pdf", pdf.FileName);

    private static void MapEntity<TCategory, TType, TTypeCategory, TTypeInfo, TChoice, TElement, TInfo, TElementCategory, TAccess>(RouteGroupBuilder api, string name,
        bool elements = true, Func<ErpDbContext, Func<ElementDto, CancellationToken, Task>>? check = null)
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
        EntityService<TCategory, TType, TTypeCategory, TTypeInfo, TChoice, TElement, TInfo, TElementCategory, TAccess> Service(HttpContext http)
        {
            var db = http.RequestServices.GetRequiredService<ErpDbContext>();
            return new(db, http.RequestServices.GetRequiredService<RequestContext>(), http.RequestServices.GetRequiredService<Visibility>()) { Check = check?.Invoke(db) };
        }

        var group = api.MapGroup($"/{name}").WithTags(name);
        // Types and categories are changed from the register's Set up screen (transaction types from their own register); reading them stays open to the screens that use them.
        string[] setup = name == "transactions" ? ["entities"] : ["setup"];
        group.MapGet("/categories", (HttpContext http, CancellationToken ct) => Service(http).CategoriesAsync(ct));
        group.MapPost("/categories", (HttpContext http, CategoryDto dto, CancellationToken ct) => Service(http).SaveCategoryAsync(dto, ct)).RequireScreenWith(setup, [name]);
        group.MapGet("/types", (HttpContext http, CancellationToken ct) => Service(http).TypesAsync(ct));
        group.MapPost("/types", (HttpContext http, ElementTypeDto dto, CancellationToken ct) => Service(http).SaveTypeAsync(dto, ct)).RequireScreenWith(setup, [name]);
        group.MapPost("/types/{id:guid}/properties/copy", (HttpContext http, Guid id, CopyPropertiesRequest body, CancellationToken ct) =>
            Service(http).CopyPropertiesAsync(id, body.FromTypeId, body.PropertyIds, ct)).RequireScreenWith(setup, [name]);
        if (!elements)
        {
            return;
        }

        LookupEndpoints.Register<TElement>((http, category, type, search, language, page, pageSize, ct) =>
            Service(http).LookupAsync(category, type, search, language, page, pageSize, ct));

        group.MapGet("/", (HttpContext http, Guid category, Guid? type, string? search, int? page, int? pageSize, CancellationToken ct) =>
            Service(http).ElementsAsync(category, type, search, page ?? 1, pageSize ?? 50, ct));
        group.MapGet("/without-category", (HttpContext http, CancellationToken ct) => Service(http).WithoutCategoryAsync(ct));
        group.MapGet("/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) => await Service(http).ElementAsync(id, ct) is { } e ? Results.Ok(e) : Results.NotFound());
        group.MapPost("/", (HttpContext http, ElementDto dto, CancellationToken ct) => Service(http).SaveElementAsync(dto, ct));
        group.MapDelete("/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) => { await Service(http).DeleteElementAsync(id, ct); return Results.NoContent(); });
        group.MapGet("/{id:guid}/history", (HttpContext http, Guid id, CancellationToken ct) => Service(http).HistoryAsync(id, ct));
    }
}

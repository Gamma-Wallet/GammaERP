using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Configuration;

/// <summary>The settings of a document type that are not its entity's (its reversal, whether it posts).</summary>
public sealed record DocumentTypeSettings(bool NeverPosted, bool AllowsPartialReversal, bool IsReversal, Guid? ReversedDocumentTypeId, bool SendsToProduction = false, Guid? ProductionDocumentTypeId = null, bool SellsProducedProduct = false, bool StartsRepair = false, bool CanBeAccepted = false,
    bool HoldsOpenItem = false, bool PaysOpenItems = false, bool BillsByPlan = false, bool NeedsApproval = false, bool SellsAssets = false,
    bool ProducedOnProductionScreen = false);

/// <summary>
/// The company's configuration rows (VAT posting groups, payment methods and their cash desks / banks and document
/// types, a document type's from / to sides, line types, VAT groups, line fields and line locations, the line types' scopes, the posting
/// groups' scenarios and their lines, print templates, sale prices), read and written as plain JSON, a column by its name. The engine reads
/// these rows; this service only keeps them whole: an administrator writes them, they belong to the request's company, every link points at a
/// row the company sees, and a change keeps the old row as history (deleting is switching off).
/// </summary>
public sealed partial class ConfigurationService(ErpDbContext db, RequestContext request, Visibility visibility)
{
    /// <summary>The configuration models, by their name in the API.</summary>
    public static readonly IReadOnlyDictionary<string, Type> Models = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        ["vat-groups"] = typeof(VatGroup),
        ["payment-methods"] = typeof(PaymentMethod),
        ["payment-method-cost-centers"] = typeof(InterPaymentMethodCostCenter),
        ["payment-method-document-types"] = typeof(InterPaymentMethodDocumentType),
        ["document-type-from"] = typeof(DocumentTypeConfFrom),
        ["document-type-to"] = typeof(DocumentTypeConfTo),
        ["document-type-transaction-types"] = typeof(InterDocumentTypeTransactionType),
        ["document-type-vat-groups"] = typeof(InterDocumentTypeVatGroup),
        ["transaction-fills"] = typeof(TransactionFill),
        ["transaction-type-scopes"] = typeof(TransactionTypeConf),
        ["transaction-type-from"] = typeof(TransactionTypeConfFrom),
        ["transaction-type-to"] = typeof(TransactionTypeConfTo),
        ["scenarios"] = typeof(Scenario),
        ["scenario-lines"] = typeof(ScenarioConf),
        ["templates"] = typeof(Template),
        ["sale-prices"] = typeof(ProductSalePrice),
        ["sale-price-types"] = typeof(ProductSalePriceType),
        ["sale-menus"] = typeof(SaleMenu),
        ["sale-menu-categories"] = typeof(SaleMenuCategory),
        ["point-of-sale-settings"] = typeof(PointOfSaleSetting),
        ["balance-sheet-lines"] = typeof(Gamma.Erp.Models.Charts.ChartAssetsLiabilities),
        ["profit-loss-lines"] = typeof(Gamma.Erp.Models.Charts.ChartProfitLoss),
        ["pos-shows"] = typeof(PosShow),
        ["document-type-flows"] = typeof(DocumentTypeFlow),
        ["dashboard-items"] = typeof(Gamma.Erp.Models.Platform.DashboardItem),
        ["closed-periods"] = typeof(PostingLock),
        ["opportunity-stages"] = typeof(Gamma.Erp.Models.Opportunities.OpportunityStage),
        ["approval-limits"] = typeof(ApprovalLimit),
        ["bank-statement-formats"] = typeof(BankStatementFormat),
        ["repair-stages"] = typeof(RepairStage),
        ["chart-mappings"] = typeof(Gamma.Erp.Models.Charts.InterChartChart),
        ["production-routes"] = typeof(Gamma.Erp.Models.Products.ProductionRoute),
        ["user-roles"] = typeof(Gamma.Erp.Models.Users.UserCategory),
        ["migration-items"] = typeof(Gamma.Erp.Models.Platform.MigrationItem),
        ["depreciation-books"] = typeof(Gamma.Erp.Models.Depreciation.DepreciationBook),
        ["depreciation-methods"] = typeof(Gamma.Erp.Models.Depreciation.DepreciationMethod),
        ["depreciation-scenarios"] = typeof(Gamma.Erp.Models.Depreciation.DepreciationScenario),
        ["depreciation-scenario-lines"] = typeof(Gamma.Erp.Models.Depreciation.DepreciationScenarioConf),
        ["group-purchase-settings"] = typeof(GroupPurchaseSetting),
        ["unit-conversions"] = typeof(Gamma.Erp.Models.Units.UnitConversion),
        ["product-recipes"] = typeof(ProductRecipe),
        ["vat-return-boxes"] = typeof(VatReturnBox),
        ["vat-return-box-rules"] = typeof(VatReturnBoxRule),
        ["menus"] = typeof(Menu),
        ["menu-access"] = typeof(Gamma.Erp.Models.Users.InterMenuUser),
    };

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly MethodInfo SetOf = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;
    private static readonly HashSet<string> Kept = [nameof(Row.Id), nameof(Row.TenantId), nameof(Row.OwnerId), nameof(Row.IdChange), nameof(Row.CreatedOn), nameof(Row.CreatedBy),
        nameof(Row.Active), "CompanyId"];

    /// <summary>The active rows of a model the company's configuration holds, each equal to the filters given (a column → a value).</summary>
    public async Task<IReadOnlyList<JsonObject>> ListAsync(string model, IReadOnlyDictionary<string, string> filters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var type = TypeOf(model);
        if (!request.IsAdministrator && (type == typeof(Models.Platform.Menu) || type == typeof(Gamma.Erp.Models.Users.InterMenuUser)))
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator sees the whole menu.");
        }

        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var rows = (await Query(type).ToListAsync(ct).ConfigureAwait(false)).Where(r => r.Active && (r.OwnerId is null || nodes.Contains(r.OwnerId.Value))).ToList();
        foreach (var (column, value) in filters)
        {
            var property = Property(type, column) ?? throw ValidationException.For(column, $"{model} has no column {column}.");
            rows = [.. rows.Where(r => Matches(property.GetValue(r), value))];
        }

        return [.. rows.Select(ToJson)];
    }

    public async Task<JsonObject?> RowAsync(string model, Guid id, CancellationToken ct) => await VisibleAsync(TypeOf(model), id, ct).ConfigureAwait(false) is { } row ? ToJson(row) : null;

    /// <summary>A new row (no id) or a change (its id), the columns given; the rest stay as they are (a new row's take their defaults).</summary>
    public async Task<JsonObject> SaveAsync(string model, JsonObject input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        Administrator();
        var type = TypeOf(model);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("company", "Configuration belongs to a company: choose one of its nodes.");
        Row row;
        if (input["id"] is { } idNode && Guid.TryParse(idNode.GetValue<string>(), out var id))
        {
            var found = await VisibleAsync(type, id, ct).ConfigureAwait(false) ?? throw ValidationException.For("id", $"There is no such {model} row.");
            row = (Row)(await db.FindAsync(type, [found.Id], ct).ConfigureAwait(false))!;
        }
        else
        {
            row = (Row)Activator.CreateInstance(type, nonPublic: true)!;
            (row.TenantId, row.OwnerId) = (request.TenantId, company);
            type.GetProperty("CompanyId")?.SetValue(row, company);
            db.Add(row);
        }

        var errors = new Dictionary<string, string>();
        foreach (var (name, value) in input)
        {
            var property = Property(type, name);
            if (property is null || Kept.Contains(property.Name))
            {
                if (property is null && !string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                {
                    errors[name] = $"{model} has no column {name}.";
                }

                continue;
            }

            try
            {
                property.SetValue(row, FromJson(value, property.PropertyType));
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or JsonException or ArgumentException or OverflowException)
            {
                errors[name] = $"is not a valid {Nullable.GetUnderlyingType(property.PropertyType)?.Name ?? property.PropertyType.Name}.";
            }
        }

        TranslatedText.Complete(row, await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false));
        await CheckLinksAsync(type, row, errors, ct).ConfigureAwait(false);
        if (row is Gamma.Erp.Models.Units.UnitConversion conversion && (conversion.Factor <= 0 || conversion.FromUnitId == conversion.ToUnitId))
        {
            errors["factor"] = "A conversion is between two units, by a factor above 0.";
        }
        else if (row is Gamma.Erp.Models.Units.UnitConversion { ProductId: null } pair
            && !await Stock.UnitConversionService.SameTreeAsync(db, pair.FromUnitId, pair.ToUnitId, ct).ConfigureAwait(false))
        {
            // A product's own conversion may cross trees (a pack of this coffee is 250 g); one for every product may not.
            errors["toUnitId"] = "A conversion for every product is between units of one tree (cm and m, not cm and kg); name the product for one across trees.";
        }

        // The founder (2026-11-01): a document type can be made from another only when it holds the same transactions.
        if (row is DocumentTypeFlow flow)
        {
            var source = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking().Where(l => l.DocumentTypeId == flow.SourceTypeId && l.TransactionTypeId != null)
                .Select(l => l.TransactionTypeId!.Value).ToListAsync(ct).ConfigureAwait(false);
            var target = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking().Where(l => l.DocumentTypeId == flow.TargetTypeId && l.TransactionTypeId != null)
                .Select(l => l.TransactionTypeId!.Value).ToListAsync(ct).ConfigureAwait(false);
            var missing = source.Except(target).ToList();
            if (source.Count == 0)
            {
                errors["sourceTypeId"] = "Define the document type's transactions first.";
            }
            else if (missing.Count > 0)
            {
                var names = await db.Set<TransactionType>().AsNoTracking().Where(t => missing.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false);
                errors["targetTypeId"] = $"This document type does not have the source's {string.Join(", ", names.Select(t => t.Name.In(TranslatedText.DefaultLanguage)))} transactions.";
            }
        }

        if (row is Menu { IsElement: true } item && string.IsNullOrWhiteSpace(item.Screen) && item.DocumentCategoryId is null)
        {
            errors["screen"] = "A menu item opens a screen: name it, or the document category it lists.";
        }

        // A dashboard item needs what its kind reads: a balance its client and supplier category, the others (but drafts and overdue) a document category.
        if (row is Models.Platform.DashboardItem shownItem)
        {
            if (shownItem.Kind == Reports.DashboardService.Balance && shownItem.PartnerCategoryId is null)
            {
                errors["partnerCategoryId"] = "A balance item needs a client and supplier category.";
            }
            else if (shownItem.Kind is not (Reports.DashboardService.Balance or Reports.DashboardService.Drafts or Reports.DashboardService.Overdue) && shownItem.DocumentCategoryId is null)
            {
                errors["documentCategoryId"] = "This kind of item needs a document category.";
            }
        }

        // One active print template per document type: printing takes it.
        if (row is Models.Platform.Template template && template.Active && template.DocumentTypeId is { } templateType
            && await db.Set<Models.Platform.Template>().AsNoTracking().AnyAsync(t => t.Active && t.DocumentTypeId == templateType && t.Id != template.Id, ct).ConfigureAwait(false))
        {
            errors["documentTypeId"] = "This document type has an active print template already: switch that one off first.";
        }

        if (row is ProductRecipe recipe &&(recipe.Quantity <= 0 || recipe.WastePercent < 0 || recipe.ComponentId == recipe.ProductId))
        {
            errors["quantity"] = "An ingredient is another product, in a quantity above 0, with a waste of 0 % or more.";
        }

        if (row is Gamma.Erp.Models.Opportunities.OpportunityStage stage && (stage.Chance < 0 || stage.Chance > 100 || stage.Outcome is < 0 or > 2))
        {
            errors["chance"] = "A stage's chance is 0 to 100 %, and its outcome 0 (open), 1 (won) or 2 (lost).";
        }

        if (row is ProductRecipe taken && (taken.RecoveredPercent < 0 || taken.RecoveredPercent > 100))
        {
            errors["recoveredPercent"] = "What comes back on taking the product apart is 0 to 100 %.";
        }

        // A formula that cannot be read is refused when it is written.
        if (row is Gamma.Erp.Models.Depreciation.DepreciationMethod method)
        {
            try
            {
                Depreciation.Formula.Evaluate(method.Formula ?? string.Empty, Depreciation.Formula.Sample);
            }
            catch (ValidationException)
            {
                errors["formula"] = $"The formula can't be worked out; it may use {string.Join(", ", Depreciation.Formula.Sample.Keys)}.";
            }
        }

        if (row is Gamma.Erp.Models.Platform.DashboardItem dashboardItem && !Reports.DashboardService.Kinds.Contains(dashboardItem.Kind))
        {
            errors["kind"] = $"The dashboard can work out {string.Join(", ", Reports.DashboardService.Kinds)}.";
        }

        if (row is Gamma.Erp.Models.Platform.MigrationItem step && !Migration.MigrationService.Kinds.Contains(step.Kind))
        {
            errors["kind"] = $"A migration step reads {string.Join(", ", Migration.MigrationService.Kinds)}.";
        }

        if (row is VatReturnBox box && box.Amount is not (1 or 2))
        {
            errors["amount"] = "A box holds the taxable value (1) or the VAT (2).";
        }

        if (errors.Count > 0)
        {
            // Nothing of a refused row stays in the context for a later save.
            db.ChangeTracker.Clear();
            throw new ValidationException(errors);
        }

        await SaveOrRefuseAsync(ct).ConfigureAwait(false);
        return ToJson(row);
    }

    /// <summary>Switches a row off (nothing is deleted); false when the company has no such row.</summary>
    public async Task<bool> DeleteAsync(string model, Guid id, CancellationToken ct)
    {
        Administrator();
        var type = TypeOf(model);
        if (await VisibleAsync(type, id, ct).ConfigureAwait(false) is null)
        {
            return false;
        }

        var row = (Row)(await db.FindAsync(type, [id], ct).ConfigureAwait(false))!;
        if (row is Models.Opportunities.OpportunityStage
            && await db.Set<Models.Opportunities.Opportunity>().AsNoTracking().AnyAsync(o => o.Active && o.StageId == id, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("active", "Opportunities are in this stage: move them to another stage first.");
        }

        row.Active = false;
        await SaveOrRefuseAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>A document type's own settings (whether it posts, its reversal); the database keeps one reversal type per type.</summary>
    public async Task<DocumentTypeSettings> SetDocumentTypeAsync(Guid id, DocumentTypeSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Administrator();
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var type = await db.Set<DocumentType>().FirstOrDefaultAsync(t => t.Id == id && t.Active, ct).ConfigureAwait(false);
        if (type is null || (type.OwnerId is { } owner && !nodes.Contains(owner)))
        {
            throw ValidationException.For("id", "There is no such document type.");
        }

        if (settings.ReversedDocumentTypeId is { } reversed && !await db.Set<DocumentType>().AnyAsync(t => t.Id == reversed && t.Active && t.OwnerId == type.OwnerId, ct)
                .ConfigureAwait(false))
        {
            throw ValidationException.For("reversedDocumentTypeId", "The type it reverses is not a type of the same company.");
        }

        (type.NeverPosted, type.AllowsPartialReversal, type.IsReversal, type.ReversedDocumentTypeId) =
            (settings.NeverPosted, settings.AllowsPartialReversal, settings.IsReversal, settings.ReversedDocumentTypeId);
        (type.SendsToProduction, type.ProductionDocumentTypeId, type.SellsProducedProduct, type.StartsRepair, type.CanBeAccepted) = (settings.SendsToProduction, settings.ProductionDocumentTypeId, settings.SellsProducedProduct, settings.StartsRepair, settings.CanBeAccepted);
        (type.HoldsOpenItem, type.PaysOpenItems, type.BillsByPlan, type.NeedsApproval, type.SellsAssets) = (settings.HoldsOpenItem, settings.PaysOpenItems, settings.BillsByPlan, settings.NeedsApproval, settings.SellsAssets);
        type.ProducedOnProductionScreen = settings.ProducedOnProductionScreen;
        await SaveOrRefuseAsync(ct).ConfigureAwait(false);
        return settings;
    }

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("configuration", $"{ErrorCodes.NotAllowedError}: only an administrator changes the configuration.");
        }
    }

    // The database's own rules (one reversal type per type, a reversal type names its type, a link's row exists) come back as a refusal.
    private async Task SaveOrRefuseAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException p)
        {
            db.ChangeTracker.Clear();
            throw ValidationException.For(p.ConstraintName ?? "row", $"{ErrorCodes.NotConfiguredError}: the database refuses it ({p.ConstraintName ?? p.SqlState}).");
        }
    }

    /// <summary>Every link of the row (its foreign keys, from the model) points at a row the company sees: its own, its organisation's, or shared.</summary>
    private async Task CheckLinksAsync(Type type, Row row, Dictionary<string, string> errors, CancellationToken ct)
    {
        var nodes = (await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false)).Concat(await visibility.NodesAsync(ct).ConfigureAwait(false)).ToHashSet();
        foreach (var key in db.Model.FindEntityType(type)!.GetForeignKeys().Where(k => k.Properties.Count == 1))
        {
            var property = key.Properties[0];
            if (Kept.Contains(property.Name) || property.PropertyInfo?.GetValue(row) is not Guid target)
            {
                continue;
            }

            var principal = key.PrincipalEntityType.ClrType;
            if (!typeof(Row).IsAssignableFrom(principal))
            {
                continue;
            }

            var found = await Query(principal).Where(r => r.Id == target).Select(r => new { r.OwnerId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (found is null || (found.OwnerId is { } owner && !nodes.Contains(owner)))
            {
                errors[Camel(property.Name)] = $"{ErrorCodes.NotConfiguredError}: there is no such {principal.Name} for this company.";
            }
        }
    }

    private async Task<Row?> VisibleAsync(Type type, Guid id, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var row = await Query(type).FirstOrDefaultAsync(r => r.Id == id && r.Active, ct).ConfigureAwait(false);
        return row is not null && (row.OwnerId is null || nodes.Contains(row.OwnerId.Value)) ? row : null;
    }

    private IQueryable<Row> Query(Type type) => ((IQueryable<Row>)SetOf.MakeGenericMethod(type).Invoke(db, null)!).AsNoTracking();

    private static Type TypeOf(string model) =>
        Models.TryGetValue(model, out var type) ? type : throw ValidationException.For("model", $"There is no configuration {model}; there are: {string.Join(", ", Models.Keys)}.");

    private static IEnumerable<PropertyInfo> Columns(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0);

    private static PropertyInfo? Property(Type type, string name) => Columns(type).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    // A filter compares values: numbers as numbers (20 is 20.00), ids and yes / no as they are, texts case aside.
    private static bool Matches(object? value, string filter) => value switch
    {
        null => string.IsNullOrEmpty(filter) || string.Equals(filter, "null", StringComparison.OrdinalIgnoreCase),
        decimal or double or float or int or long => decimal.TryParse(filter, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
            && Convert.ToDecimal(value, CultureInfo.InvariantCulture) == n,
        Guid g => Guid.TryParse(filter, out var f) && f == g,
        bool b => bool.TryParse(filter, out var f) && f == b,
        IFormattable f => string.Equals(f.ToString(null, CultureInfo.InvariantCulture), filter, StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(value.ToString(), filter, StringComparison.OrdinalIgnoreCase),
    };

    private static JsonObject ToJson(Row row)
    {
        var json = new JsonObject();
        foreach (var property in Columns(row.GetType()))
        {
            json[Camel(property.Name)] = property.GetValue(row) switch
            {
                null => null,
                TranslatedText text => new JsonObject(text.Values.Select(v => KeyValuePair.Create(v.Key, (JsonNode?)JsonValue.Create(v.Value)))),
                JsonNode node => node.DeepClone(),
                var value => JsonSerializer.SerializeToNode(value, property.PropertyType),
            };
        }

        return json;
    }

    private static object? FromJson(JsonNode? value, Type target)
    {
        if (value is null)
        {
            return null;
        }

        if (target == typeof(TranslatedText))
        {
            return new TranslatedText { Values = new Dictionary<string, string>(value.Deserialize<Dictionary<string, string>>()!, StringComparer.OrdinalIgnoreCase) };
        }

        return target == typeof(JsonNode) ? value.DeepClone() : value.Deserialize(target, Web);
    }
}

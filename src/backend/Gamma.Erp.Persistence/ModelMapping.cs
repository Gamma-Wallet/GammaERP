using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using TypeInfo = Gamma.Erp.Models.Base.TypeInfo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gamma.Erp.Persistence;

/// <summary>
/// Maps every model of Gamma.Erp.Models to its table, the same way for all: the table its [Table] names, in the schema of its folder, snake_case columns, a GUID key
/// given by the application, texts and property values as jsonb, exact decimals, and a foreign key for every link found by its name.
/// Weak links (from, to, a transaction's element…) get none, because which table they point at depends on the configuration.
/// Reads only ever see active rows; inactive ones are deleted (soft) or history copies.
/// </summary>
public static class ModelMapping
{
    /// <summary>Every model that is a table.</summary>
    public static IReadOnlyList<Type> Tables { get; } = [.. typeof(Row).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Row).IsAssignableFrom(t) && t.GetCustomAttribute<TableAttribute>() is not null)
        .OrderBy(t => t.FullName, StringComparer.Ordinal)];

    private static readonly Dictionary<string, Type> ByName = Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>The model folders of the entities with their nine models, and the class prefix of each.</summary>
    private static readonly Dictionary<string, string> Entities = new(StringComparer.Ordinal)
    {
        ["Documents"] = "Document", ["Transactions"] = "Transaction", ["Products"] = "Product", ["Partners"] = "Partner",
        ["CostCenters"] = "CostCenter", ["Charts"] = "Chart", ["Employees"] = "Employee", ["Users"] = "User", ["Units"] = "Unit",
    };

    /// <summary>Links whose target depends on the configuration (no foreign key), as Model.Property, or any model's Property.</summary>
    private static readonly HashSet<string> Weak = new(StringComparer.Ordinal)
    {
        "FromId", "ToId", "Transaction.ElementId", "Transaction.ElementTypeId", "Elements.CategoryId", "Elements.TypeId", "Elements.RecordId",
        "ElementsAccess.CategoryId",
    };

    /// <summary>Link names that do not spell their target's class.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["Document1"] = "Document", ["Document2"] = "Document", ["Currency2"] = "Currency", ["Book"] = "DepreciationBook", ["SecondCategory"] = "DocumentCategory", ["AssetChart"] = "Chart", ["DepreciationChart"] = "Chart",
        ["Method"] = "DepreciationMethod", ["DocumentTypeTransactionType"] = "InterDocumentTypeTransactionType",
        ["ValueElements"] = "Elements", ["Chart1"] = "Chart", ["Chart2"] = "Chart", ["ReversedDocumentType"] = "DocumentType", ["ReferenceDocumentType"] = "DocumentType", ["ReversedDocument"] = "Document", ["PointOfSale"] = "CostCenter", ["Table"] = "CostCenter", ["BalanceValueElements"] = "Elements", ["ProductValueElements"] = "Elements", ["ProductSpecificUnit"] = "InterProductSpecificUnit", ["ProductTypeUnit"] = "InterProductTypeUnit", ["GroupMember"] = "CostCenter", ["FromUnit"] = "Unit", ["ToUnit"] = "Unit", ["Component"] = "Product", ["ComponentUnit"] = "Unit", ["Order"] = "Document", ["Box"] = "VatReturnBox",
        ["PaymentMethodDocumentType"] = "InterPaymentMethodDocumentType", ["TypeConf"] = "TransactionTypeConf", ["Company"] = "CostCenter",
        ["Intake"] = "Document", ["CashBank"] = "CostCenter", ["PreviousCurrency"] = "Currency", ["Technician"] = "Employee", ["ProductionPoint"] = "CostCenter", ["BillLine"] = "Transaction", ["ProductionDocumentType"] = "DocumentType", ["ProductionDocument"] = "Document", ["OrderType"] = "DocumentType", ["Bank"] = "CostCenter", ["Format"] = "BankStatementFormat", ["Statement"] = "BankStatement", ["DefaultCostCenter"] = "CostCenter",
        ["User"] = "User", ["Scenario"] = "Scenario", ["DepreciationBook"] = "DepreciationBook",
        ["SourceType"] = "DocumentType", ["TargetType"] = "DocumentType", ["ValidUntilInfo"] = "DocumentTypeInfo", ["ElementDebitValueElements"] = "Elements", ["ElementCreditValueElements"] = "Elements", ["PointOfSaleType"] = "CostCenterType", ["Auditorium"] = "CostCenter", ["Place"] = "CostCenter", ["Show"] = "PosShow", ["Bill"] = "Document", ["SaleType"] = "DocumentType", ["PurchaseType"] = "DocumentType", ["Warehouse"] = "CostCenter", ["GroupSale"] = "Document", ["BillType"] = "DocumentType", ["MoneyInType"] = "DocumentType", ["MoneyOutType"] = "DocumentType", ["CashDesk"] = "CostCenter", ["WalkInClient"] = "Partner",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Links the mapping could not resolve to a table (reported, and left without a foreign key).</summary>
    public static List<string> Unresolved { get; } = [];

    public static void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        Unresolved.Clear();
        foreach (var type in Tables)
        {
            var entity = modelBuilder.Entity(type);
            var table = type.GetCustomAttribute<TableAttribute>()!.Name;
            var schema = Schema(type);
            entity.ToTable(table, schema);
            if (type.GetProperty("UserId") is not null && type.GetProperty("RoleId") is not null)
            {
                // Access is given to one user or to a role, never both, never neither.
                entity.ToTable(table, schema, t => t.HasCheckConstraint($"ck_{table}_user_or_role", "(user_id IS NULL) <> (role_id IS NULL)"));
            }
            entity.HasKey(nameof(Row.Id));
            entity.Property(nameof(Row.Id)).ValueGeneratedNever();
            entity.Property(nameof(Row.Active)).HasDefaultValue(true);
            entity.HasIndex(nameof(Row.TenantId));
            entity.HasIndex(nameof(Row.OwnerId));
            typeof(ModelMapping).GetMethod(nameof(OnlyActive), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [entity]);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Column(entity, property);
                Link(modelBuilder, type, property);
            }
        }

        // A user signs in by name before the corporate is known, so a name is one user's across the whole system (history copies aside).
        // Reversal types (the founder's rules, 2026-10-30): a reversal type reverses exactly one type and every other type none; no two active
        // reversal types reverse the same type; a reversal's reversed number goes with the document it reverses.
        modelBuilder.Entity<Gamma.Erp.Models.Documents.DocumentType>().HasIndex(t => t.ReversedDocumentTypeId).IsUnique()
            .HasFilter("active AND reversed_document_type_id IS NOT NULL").HasDatabaseName("ux_document_type_reversed");
        modelBuilder.Entity<Gamma.Erp.Models.Documents.DocumentType>().ToTable(t => t.HasCheckConstraint("ck_document_type_reversal",
            "is_reversal = (reversed_document_type_id IS NOT NULL)"));
        modelBuilder.Entity<Gamma.Erp.Models.Documents.Document>().ToTable(t => t.HasCheckConstraint("ck_document_reversed",
            "reversed_document_code IS NULL OR reversed_document_id IS NOT NULL"));
        modelBuilder.Entity<Gamma.Erp.Models.Users.User>().HasIndex(u => u.Username).IsUnique().HasFilter("active AND username IS NOT NULL")
            .HasDatabaseName("ux_user_username");
    }

    private static void OnlyActive<T>(EntityTypeBuilder builder)
        where T : Row => builder.HasQueryFilter((System.Linq.Expressions.Expression<Func<T, bool>>)(r => r.Active));

    private static void Column(EntityTypeBuilder entity, PropertyInfo property)
    {
        var type = property.PropertyType;
        if (type == typeof(TranslatedText))
        {
            entity.Property(property.Name).HasColumnType("jsonb").HasConversion(TextConverter, TextComparer);
        }
        else if (type == typeof(JsonNode))
        {
            entity.Property(property.Name).HasColumnType("jsonb").HasConversion(NodeConverter, NodeComparer);
        }
        else if (Nullable.GetUnderlyingType(type) == typeof(decimal) || type == typeof(decimal))
        {
            // Amounts and quantities exactly; rates and percentages with more places.
            var rate = property.Name.Contains("Rate", StringComparison.Ordinal) || property.Name.Contains("Percentage", StringComparison.Ordinal)
                || property.Name.Contains("Latitude", StringComparison.Ordinal) || property.Name.Contains("Longitude", StringComparison.Ordinal);
            entity.Property(property.Name).HasPrecision(rate ? 28 : 19, rate ? 10 : 6);
        }
    }

    private static void Link(ModelBuilder modelBuilder, Type owner, PropertyInfo property)
    {
        if (!property.Name.EndsWith("Id", StringComparison.Ordinal) && property.Name is not (nameof(Row.IdSup) or nameof(Row.IdChange) or nameof(Row.CreatedBy)))
        {
            return;
        }

        if (property.Name == nameof(Row.Id) || property.Name == nameof(Row.TenantId) || Weak.Contains(property.Name) || Weak.Contains($"{owner.Name}.{property.Name}")
            || (property.PropertyType != typeof(Guid) && property.PropertyType != typeof(Guid?)))
        {
            return;
        }

        if (Target(owner, property.Name) is not { } target)
        {
            Unresolved.Add($"{owner.Name}.{property.Name}");
            return;
        }

        // Nothing is ever deleted, so a link never cascades.
        var table = owner.GetCustomAttribute<TableAttribute>()!.Name;
        modelBuilder.Entity(owner).HasOne(target).WithMany().HasForeignKey(property.Name).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(Name($"fk_{table}_{Snake(property.Name)}"));
    }

    /// <summary>The table a link points at: within its entity for the nine models' links, else by the link's name.</summary>
    private static Type? Target(Type owner, string property)
    {
        if (property is nameof(Row.IdSup) or nameof(Row.IdChange))
        {
            return owner;
        }

        // Every row belongs to a node of the organisation tree.
        if (property == nameof(Row.OwnerId))
        {
            return ByName.GetValueOrDefault("CostCenter");
        }

        if (property is nameof(Row.CreatedBy) or "UserId")
        {
            return ByName.GetValueOrDefault("User");
        }

        // A role is a user category: a user can hold several.
        if (property == "RoleId")
        {
            return ByName.GetValueOrDefault("UserCategory");
        }

        var folder = owner.Namespace?.Split('.').Last();
        if (folder is not null && Entities.TryGetValue(folder, out var p))
        {
            var within = (property, owner.BaseType?.Name) switch
            {
                ("TypeId", nameof(Element) or nameof(TypeInfo) or nameof(InterTypeCategory)) => $"{p}Type",
                ("CategoryId", nameof(InterTypeCategory) or nameof(InterElementCategory)) => $"{p}Category",
                ("ElementId", nameof(ElementInfo) or nameof(InterElementCategory) or nameof(ElementAccess)) => p,
                ("TypeInfoId", nameof(ElementInfo) or nameof(TypeInfoChoice)) => $"{p}TypeInfo",
                ("ShownForChoiceId", nameof(TypeInfo)) => $"{p}TypeInfoChoice",
                _ => null,
            };
            if (within is not null)
            {
                return ByName.GetValueOrDefault(within);
            }
        }

        // Plain tables that follow the type / property / choice pattern of the entities.
        if (owner.Name.EndsWith("TypeInfoChoice", StringComparison.Ordinal) && property == "TypeInfoId")
        {
            return ByName.GetValueOrDefault(owner.Name[..^"Choice".Length]);
        }

        if (owner.Name.EndsWith("TypeInfo", StringComparison.Ordinal) && property == "ShownForChoiceId")
        {
            return ByName.GetValueOrDefault(owner.Name + "Choice");
        }

        if (owner.Name.EndsWith("TypeInfo", StringComparison.Ordinal) && property == "TypeId")
        {
            return ByName.GetValueOrDefault(owner.Name[..^"Info".Length]);
        }

        var name = property[..^2];
        return ByName.GetValueOrDefault(Aliases.GetValueOrDefault(name, name)) ?? ByName.GetValueOrDefault("Inter" + name);
    }

    /// <summary>The schema of a model: its folder in Gamma.Erp.Models, in snake_case (Documents → documents, CostCenters → cost_centers).</summary>
    public static string Schema(Type model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Snake(model.Namespace!.Split('.').Last());
    }

    /// <summary>A database name within PostgreSQL's 63 characters: cut, with a short hash of the whole so it stays unique.</summary>
    private static string Name(string name)
    {
        if (name.Length <= 63)
        {
            return name;
        }

        // FNV-1a: the same on every run, so migrations never rename a constraint.
        var hash = 2166136261u;
        foreach (var c in name)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return $"{name[..54]}_{hash:x8}";
    }

    private static string Snake(string name) => string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static readonly ValueConverterFor<TranslatedText, string> TextConverter = new(
        t => JsonSerializer.Serialize(t.Values, Json),
        s => new TranslatedText { Values = new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(s, Json) ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase) });

    private static readonly ValueComparer<TranslatedText> TextComparer = new(
        (a, b) => JsonSerializer.Serialize(a!.Values, Json) == JsonSerializer.Serialize(b!.Values, Json),
        t => JsonSerializer.Serialize(t.Values, Json).GetHashCode(StringComparison.Ordinal),
        t => new TranslatedText { Values = new Dictionary<string, string>(t.Values, StringComparer.OrdinalIgnoreCase) });

    private static readonly ValueConverterFor<JsonNode, string> NodeConverter = new(n => n.ToJsonString(Json), s => JsonNode.Parse(s, null, default)!);

    private static readonly ValueComparer<JsonNode> NodeComparer = new(
        (a, b) => JsonNode.DeepEquals(a, b), n => n.ToJsonString(Json).GetHashCode(StringComparison.Ordinal), n => n.DeepClone());

    private sealed class ValueConverterFor<TModel, TProvider>(
        System.Linq.Expressions.Expression<Func<TModel, TProvider>> toProvider, System.Linq.Expressions.Expression<Func<TProvider, TModel>> fromProvider)
        : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TModel, TProvider>(toProvider, fromProvider);
}

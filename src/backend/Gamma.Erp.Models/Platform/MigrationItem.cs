using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

/// <summary>
/// A step of the migration, per company: what list it holds and which steps must have data first. The order lives here; the engine knows only
/// how to read and write each kind of list.
/// </summary>
[Table("tbl_migration_item")]
public sealed class MigrationItem : Row
{
    public string? Code { get; set; }

    public TranslatedText? Name { get; set; }

    /// <summary>How the list is read and written: Elements, Categories, DocumentTypes, Types, LineKinds, PostingGroups, Prices, Rates, Numbers, Opening, OpeningStock, OpeningAssets, VatGroups, DocumentVat, VatBoxes, VatRules.</summary>
    public string? Kind { get; set; }

    /// <summary>The entity of an Elements or Categories list (partners, products, cost-centers, units, employees); of an Opening list its line kinds, separated by commas.</summary>
    public string? Entity { get; set; }

    /// <summary>The codes of the steps this one needs, separated by commas: the dependency tree is built from these.</summary>
    public string? Requires { get; set; }

    /// <summary>An Opening list: the code of the account its sheet balances on, which stands at zero once every step is in.</summary>
    public string? OffsetAccount { get; set; }

    /// <summary>An Opening list: the journal type of its rows, under a sheet type.</summary>
    public Guid? DocumentTypeId { get; set; }

    public double SortOrder { get; set; }
}

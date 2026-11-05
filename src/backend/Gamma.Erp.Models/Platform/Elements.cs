using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

[Table("tbl_elements")]
public sealed class Elements : Row
{
    public Guid? ElementsTypeId { get; set; }

    public TranslatedText? Name { get; set; }

    public string? Table { get; set; }

    public string? Category { get; set; }

    public Guid? CategoryId { get; set; }

    public string? Type { get; set; }

    public Guid? TypeId { get; set; }

    public string? Record { get; set; }

    public Guid? RecordId { get; set; }

    public int? DocumentConf { get; set; }

    /// <summary>The number the row is known by, e.g. the old value number of a "Transaction amount" row (1 value, 2 discount, 3 VAT…). New.</summary>
    public int? Code { get; set; }
}

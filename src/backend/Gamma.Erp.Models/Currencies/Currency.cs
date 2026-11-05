using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Currencies;

/// <summary>A currency, e.g. ALL (the base), EUR, USD.</summary>
[Table("tbl_currency")]
public sealed class Currency : Row
{
    public string? Code { get; set; }

    public TranslatedText? Name { get; set; }

    /// <summary>The company's own currency, the one amounts are kept in besides the document's.</summary>
    public bool IsBase { get; set; }
}

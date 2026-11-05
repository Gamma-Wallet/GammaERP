using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

[Table("tbl_elements_type")]
public sealed class ElementsType : Row
{
    public TranslatedText? Name { get; set; }
}

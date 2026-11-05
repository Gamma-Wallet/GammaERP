using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A box of the country's VAT return form (e.g. Albania's "Formulari i Deklarimit dhe Pagesës së TVSH-së", box 13 "Shitje me shkallë 20%"):
/// what it is called and in which order, whether it is on the sales (output) or the purchases (input) side and whether it holds the taxable
/// value or the VAT. What fills it is its rules (<see cref="VatReturnBoxRule"/>); a box with none stays 0. New (the founder, 2026-10-30:
/// the VAT return is per country); configuration, so each company can change it.
/// </summary>
[Table("tbl_vat_return_box")]
public sealed class VatReturnBox : Row
{
    /// <summary>The box's number on the form.</summary>
    public string? Code { get; set; }

    public TranslatedText? Name { get; set; }

    public double SortOrder { get; set; }

    /// <summary>True for the purchases (input VAT) side, false for sales (output VAT).</summary>
    public bool IsInput { get; set; }

    /// <summary>What the box holds of each line: 1 the taxable value, 2 the VAT.</summary>
    public int Amount { get; set; }
}

namespace Gamma.Erp.Models.Base;

/// <summary>One choice of a list property, e.g. ADSL, ADSL 2, ADSL 2+ of "DSL type".</summary>
public abstract class TypeInfoChoice : Row
{
    public Guid TypeInfoId { get; set; }

    /// <summary>The choice as shown.</summary>
    public TranslatedText Name { get; set; } = new();

    public TranslatedText? Description { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

/// <summary>
/// A company's skin (the founder, 2026-11-04): the values of the screens' design tokens, for the ERP or for the point of
/// sale. It changes colours, fonts, corner roundness and density only, never the layout. An administrator uploads skins and chooses the one in use
/// per kind; the starter packs give every company Harbour (in use) and Nightshift (dark).
/// </summary>
[Table("tbl_skin")]
public sealed class Skin : Row
{
    public string? Name { get; set; }

    /// <summary>Where it applies: "erp" (every screen but the point of sale) or "pos".</summary>
    public string Kind { get; set; } = "erp";

    /// <summary>A dark skin (light text on dark surfaces).</summary>
    public bool Dark { get; set; }

    /// <summary>The skin the company uses for its kind: at most one per company and kind.</summary>
    public bool InUse { get; set; }

    /// <summary>The token values as a JSON object, token name (without the leading --) → value.</summary>
    public string Tokens { get; set; } = "{}";
}

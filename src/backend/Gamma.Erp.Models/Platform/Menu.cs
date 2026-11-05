using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

/// <summary>
/// A node of the company's menu: one tree (<see cref="Row.IdSup"/>), folders and the items that open a screen. What an item
/// opens is configuration: the screen's key and its parameter, or a document category, whose item lists
/// the document types the user was given, so a new type shows without a menu change. Reports are items too. Who sees a node
/// is <see cref="Users.InterMenuUser"/>: a node given to a user or a role brings everything under it and the folders above it.
/// </summary>
[Table("tbl_menu")]
public sealed class Menu : Row
{
    public TranslatedText? Name { get; set; }

    public string? Code { get; set; }

    /// <summary>True for an item that opens a screen, false for a folder.</summary>
    public bool IsElement { get; set; }

    public double SortOrder { get; set; }

    /// <summary>The key of the screen the item opens, e.g. "documents", "entities", "config", "report".</summary>
    public string? Screen { get; set; }

    /// <summary>What the screen shows, e.g. the entity "products", the configuration model "vat-groups", the report "trial-balance".</summary>
    public string? Parameter { get; set; }

    /// <summary>For a documents item: the category whose document types it lists.</summary>
    public Guid? DocumentCategoryId { get; set; }

    /// <summary>The name of the node's icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Only an administrator sees this node: it is shown to no one else and can't be given.</summary>
    public bool AdminOnly { get; set; }
}

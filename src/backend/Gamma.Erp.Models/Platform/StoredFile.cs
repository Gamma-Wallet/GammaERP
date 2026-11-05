using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

/// <summary>
/// A file kept in the file store: an attachment, the content of a File or Photo property, a printed
/// document. The row says what it is and who may see it (its node, as every row); the bytes are in the store chosen at start-up, under a key
/// Gamma makes, never the file's own name. A property holds the file by its link, <c>/api/v1/files/{id}</c>.
/// </summary>
[Table("tbl_stored_file")]
public sealed class StoredFile : Row
{
    /// <summary>The name it was uploaded with (shown and used when downloading, never as a path).</summary>
    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Its size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>The SHA-256 of its bytes, in hex: checked when it is read back.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Where the store keeps it.</summary>
    public string StorageKey { get; set; } = "";
}

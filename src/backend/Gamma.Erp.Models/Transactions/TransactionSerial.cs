using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A serial number on a document line: as typed or picked while the document is a draft, tied to its register row when it is posted.</summary>
[Table("tbl_transaction_serial")]
public sealed class TransactionSerial : Row
{
    public Guid TransactionId { get; set; }

    public string Serial { get; set; } = string.Empty;

    public string? PartNumber { get; set; }

    /// <summary>The register row, from posting on.</summary>
    public Guid? ProductSerialId { get; set; }
}

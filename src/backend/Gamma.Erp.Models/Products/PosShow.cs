using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A show at a cinema or theatre (the founder, 2026-10-31): in one auditorium, on a day at a time, its seats sold as tickets of a product.</summary>
[Table("tbl_pos_show")]
public sealed class PosShow : Row
{
    public string? Code { get; set; }

    public TranslatedText Name { get; set; } = new();

    /// <summary>The auditorium (a cost centre): its seats are what is sold.</summary>
    public Guid AuditoriumId { get; set; }

    /// <summary>What a ticket is sold as.</summary>
    public Guid ProductId { get; set; }

    public DateOnly Day { get; set; }

    public TimeOnly? StartsAt { get; set; }

    /// <summary>The ticket's price without VAT; empty: the product's sale price.</summary>
    public decimal? Price { get; set; }
}

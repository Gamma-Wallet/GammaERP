using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Reports;
using Microsoft.EntityFrameworkCore;
using static Gamma.Erp.Services.Printing.PdfLayout;

namespace Gamma.Erp.Services.Printing;

/// <summary>A printout: its bytes and the name it is downloaded or attached with.</summary>
public sealed record PdfFile(byte[] Content, string FileName);

/// <summary>
/// Printouts as PDF, of what the request sees: a document (its type's template gives the heading and the closing text when
/// there is one; a document not posted says so), and the reports. The words in the reader's language, the accounts and names as they are.
/// </summary>
public sealed partial class PrintService(ErpDbContext db, DocumentService documents, ReportService reports, Visibility visibility,
    StockReportService stock, Partners.PartnerStatisticsService partners, Files.FileService files)
{
    public async Task<PdfFile?> DocumentAsync(Guid id, string language, CancellationToken ct)
    {
        if (await documents.DocumentAsync(id, ct).ConfigureAwait(false) is not { } document)
        {
            return null;
        }

        // A document goes out in the company's language (the law asks for it), whatever the screens are in. English is the reader's choice for the reports.
        language = await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false);
        var culture = Culture(language);
        var type = await db.Set<DocumentType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == document.TypeId, ct).ConfigureAwait(false);
        var template = await db.Set<Template>().AsNoTracking().Where(t => t.DocumentTypeId == document.TypeId && t.Active).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var title = $"{template?.LongName ?? type?.Name.In(language) ?? Word(language, "document")} {document.Code}".Trim();
        var (pdf, section) = Page(await CompanyNameAsync(document.CompanyId, language, ct).ConfigureAwait(false), title,
            $"{Word(language, "date")}: {Day(document.DocumentDate ?? document.PostingDate, culture)}", language);
        if (!document.Posted)
        {
            var mark = section.AddParagraph(Word(language, "notPosted"));
            mark.Format.Font.Bold = true;
            mark.Format.Font.Color = MigraDoc.DocumentObjectModel.Colors.DarkRed;
        }

        if (document.ReversedDocumentCode is { } reversed)
        {
            section.AddParagraph($"{Word(language, "reverses")}: {reversed}").Format.Font.Bold = true;
        }

        section.AddParagraph($"{Word(language, "from")}: {document.FromName}");
        section.AddParagraph($"{Word(language, "to")}: {document.ToName}").Format.SpaceAfter = MigraDoc.DocumentObjectModel.Unit.FromPoint(8);
        var lines = document.Transactions.Select((t, i) => ((IReadOnlyList<string>)[(i + 1).ToString(culture), t.ElementName ?? "", Number(t.Quantity, culture),
            t.UnitName ?? "", Amount(t.Price, culture), Amount(t.Value, culture), Amount(t.Vat, culture), Amount(t.ValueVat, culture)], false)).ToList();
        lines.Add(([string.Empty, Word(language, "total"), "", "", "", Amount(document.Transactions.Sum(t => t.Value ?? 0), culture),
            Amount(document.Transactions.Sum(t => t.Vat ?? 0), culture), Amount(document.Transactions.Sum(t => t.ValueVat ?? 0), culture)], true));
        Table(section, [("#", 0.8, true), (Word(language, "description"), 6.2, false), (Word(language, "quantity"), 1.6, true), (Word(language, "unit"), 1.4, false),
            (Word(language, "price"), 2, true), (Word(language, "value"), 2.2, true), (Word(language, "vat"), 1.8, true), (Word(language, "total"), 2.2, true)], lines);
        if (!string.IsNullOrWhiteSpace(template?.Body))
        {
            section.AddParagraph(template.Body).Format.SpaceBefore = MigraDoc.DocumentObjectModel.Unit.FromPoint(12);
        }

        return new PdfFile(Render(pdf), $"{FileNamePart(type?.Code ?? "document")}-{FileNamePart(document.Code ?? id.ToString("N"))}.pdf");
    }

    public async Task<PdfFile> TrialBalanceAsync(DateOnly from, DateOnly to, Guid? chart, string language, Guid? costCenter, CancellationToken ct)
    {
        var report = await reports.TrialBalanceAsync(from, to, chart, language, costCenter, ct).ConfigureAwait(false);
        var culture = Culture(language);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false), $"{Word(language, "trialBalance")} — {report.Chart}",
            Period(language, from, to, culture), language, landscape: true);
        var rows = report.Rows.Select(r => ((IReadOnlyList<string>)[r.Code ?? "", r.Name ?? "", Amount(r.OpeningDebit, culture),
            Amount(r.OpeningCredit, culture), Amount(r.Debit, culture), Amount(r.Credit, culture), Amount(r.ClosingDebit, culture), Amount(r.ClosingCredit, culture)], r.IsGroup))
            .Append(([string.Empty, Word(language, "total"), "", "", Amount(report.TotalDebit, culture), Amount(report.TotalCredit, culture), "", ""], true));
        Table(section, [(Word(language, "account"), 2.8, false), (Word(language, "name"), 8.0, false), ($"{Word(language, "opening")} {Word(language, "debit")}", 2.4, true),
            ($"{Word(language, "opening")} {Word(language, "credit")}", 2.4, true), (Word(language, "debit"), 2.4, true), (Word(language, "credit"), 2.4, true),
            ($"{Word(language, "closing")} {Word(language, "debit")}", 2.4, true), ($"{Word(language, "closing")} {Word(language, "credit")}", 2.4, true)], rows);
        return new PdfFile(Render(pdf), $"trial-balance-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile?> LedgerAsync(Guid account, DateOnly from, DateOnly to, string language, Guid? costCenter, CancellationToken ct)
    {
        if (await reports.LedgerAsync(account, from, to, language, costCenter, ct).ConfigureAwait(false) is not { } ledger)
        {
            return null;
        }

        var culture = Culture(language);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false), $"{Word(language, "ledger")} {ledger.Code} {ledger.Name}",
            Period(language, from, to, culture), language, landscape: true);
        var rows = new List<(IReadOnlyList<string>, bool)> { ([string.Empty, Word(language, "opening"), "", "", "", "", "", Amount(ledger.Opening, culture)], true) };
        rows.AddRange(ledger.Lines.Select(l => ((IReadOnlyList<string>)[Day(l.PostingDate, culture), l.Document ?? "", l.Account ?? "", l.From ?? "", l.To ?? "",
            Amount(l.Debit, culture), Amount(l.Credit, culture), Amount(l.Balance, culture)], false)));
        rows.Add(([string.Empty, Word(language, "closing"), "", "", "", Amount(ledger.Lines.Sum(l => l.Debit), culture), Amount(ledger.Lines.Sum(l => l.Credit), culture),
            Amount(ledger.Closing, culture)], true));
        Table(section, [(Word(language, "date"), 2.2, false), (Word(language, "document"), 2.6, false), (Word(language, "account"), 2, false), (Word(language, "from"), 5.2, false),
            (Word(language, "to"), 5.2, false), (Word(language, "debit"), 2.6, true), (Word(language, "credit"), 2.6, true), (Word(language, "balance"), 2.8, true)], rows);
        return new PdfFile(Render(pdf), $"ledger-{FileNamePart(ledger.Code ?? "account")}-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile> VatRegisterAsync(IReadOnlyList<Guid> categories, DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        var book = await reports.VatRegisterAsync(categories, from, to, language, ct).ConfigureAwait(false);
        var culture = Culture(language);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false), Word(language, "vatRegister"), Period(language, from, to, culture), language,
            landscape: true);
        var rows = book.Lines.Select(l => ((IReadOnlyList<string>)[Day(l.PostingDate, culture), l.ReversedDocument is { } r ? $"{l.Document} ({r})" : l.Document ?? "", l.DocumentType ?? "", l.Partner ?? "",
            string.Join(", ", l.Amounts.Select(a => a.VatGroup ?? Word(language, "noVat"))), Amount(l.Base, culture), Amount(l.Vat, culture), Amount(l.Base + l.Vat, culture)], false))
            .Concat(book.Totals.Select(t => ((IReadOnlyList<string>)[string.Empty, "", "", Word(language, "total"), t.VatGroup ?? Word(language, "noVat"), Amount(t.Base, culture),
                Amount(t.Vat, culture), Amount(t.Base + t.Vat, culture)], true)));
        Table(section, [(Word(language, "date"), 2.2, false), (Word(language, "document"), 2.6, false), (Word(language, "description"), 4.4, false),
            (Word(language, "partner"), 5.6, false), (Word(language, "vatGroup"), 3.2, false), (Word(language, "base"), 2.6, true), (Word(language, "vat"), 2.4, true),
            (Word(language, "total"), 2.6, true)], rows);
        return new PdfFile(Render(pdf), $"vat-register-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile> VatReturnAsync(IReadOnlyList<Guid> output, IReadOnlyList<Guid> input, DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        var vat = await reports.VatReturnAsync(output, input, from, to, language, ct).ConfigureAwait(false);
        var culture = Culture(language);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false), Word(language, "vatReturn"), Period(language, from, to, culture), language);
        IEnumerable<(IReadOnlyList<string>, bool)> Book(string heading, IReadOnlyList<VatAmountDto> amounts, decimal total) =>
            amounts.Select(a => ((IReadOnlyList<string>)[heading, a.VatGroup ?? Word(language, "noVat"), Amount(a.Base, culture), Amount(a.Vat, culture)], false))
                .Append(([heading, Word(language, "total"), Amount(amounts.Sum(a => a.Base), culture), Amount(total, culture)], true));
        var rows = Book(Word(language, "output"), vat.Output, vat.OutputVat).Concat(Book(Word(language, "input"), vat.Input, vat.InputVat))
            .Append(([Word(language, vat.Payable >= 0 ? "payable" : "refundable"), "", "", Amount(Math.Abs(vat.Payable), culture)], true));
        Table(section, [(Word(language, "description"), 6, false), (Word(language, "vatGroup"), 5, false), (Word(language, "base"), 3, true), (Word(language, "vat"), 3, true)], rows);
        return new PdfFile(Render(pdf), $"vat-return-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    private static string Period(string language, DateOnly from, DateOnly to, System.Globalization.CultureInfo culture) =>
        $"{Word(language, "period")}: {Day(from, culture)} – {Day(to, culture)}";

    private static string Number(decimal? value, System.Globalization.CultureInfo culture) => value is { } v ? v.ToString("0.###", culture) : "";

    /// <summary>The company's property that holds its logo (a photo): found by this code, whatever its name.</summary>
    public const string LogoCode = "LOGO";

    /// <summary>The company's details printed under its name, in this order, when they are filled in: found by code, labelled by the property's name.</summary>
    public static readonly string[] DetailCodes = ["TRADE_NAME", "TAX_NUMBER", "ADDRESS", "PHONE", "EMAIL"];

    private async Task<Letterhead> RequestCompanyAsync(string language, CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) is { } company ? await CompanyNameAsync(company, language, ct).ConfigureAwait(false) : new Letterhead("");

    /// <summary>The company's name and logo for a print; a logo that can't be read leaves the name alone.</summary>
    private async Task<Letterhead> CompanyNameAsync(Guid company, string language, CancellationToken ct)
    {
        var name = (await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == company, ct).ConfigureAwait(false))?.Name.In(language) ?? "";
        // The same places on every print (the company type's properties with these codes), each with its value when it is set.
        var type = await db.Set<CostCenter>().AsNoTracking().Where(c => c.Id == company).Select(c => (Guid?)c.TypeId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var places = await db.Set<CostCenterTypeInfo>().AsNoTracking().Where(p => p.TypeId == type && p.Active && p.Code != null && DetailCodes.Contains(p.Code))
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = places.Select(p => p.Id).ToList();
        var values = await db.Set<CostCenterInfo>().AsNoTracking().Where(v => v.ElementId == company && v.Active && ids.Contains(v.TypeInfoId))
            .Select(v => new { v.TypeInfoId, v.Value }).ToListAsync(ct).ConfigureAwait(false);
        var valueOf = values.GroupBy(v => v.TypeInfoId).ToDictionary(g => g.Key, g => g.First().Value);
        var details = DetailCodes.Select(code => places.FirstOrDefault(p => p.Code == code)).Where(p => p is not null)
            .Select(p => $"{p!.Name.In(language)}: {Shown(valueOf.GetValueOrDefault(p.Id), language)}".TrimEnd()).ToList();
        var letterhead = await LogoAsync(company, name, ct).ConfigureAwait(false);
        return letterhead with { Details = details };
    }

    /// <summary>A property's value as text: plain text, or the language's text of a translated one.</summary>
    private static string? Shown(System.Text.Json.Nodes.JsonNode? value, string language) => value switch
    {
        System.Text.Json.Nodes.JsonObject texts => (texts[language] ?? texts[Models.Base.TranslatedText.DefaultLanguage] ?? texts.FirstOrDefault().Value)?.ToString(),
        System.Text.Json.Nodes.JsonValue text => text.ToString(),
        _ => null,
    };

    private async Task<Letterhead> LogoAsync(Guid company, string name, CancellationToken ct)
    {
        var link = await (from v in db.Set<CostCenterInfo>().AsNoTracking()
                          join p in db.Set<CostCenterTypeInfo>().AsNoTracking() on v.TypeInfoId equals p.Id
                          where v.ElementId == company && v.Active && p.Active && p.Code == LogoCode
                          select v.Value).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        // The value is the file's link, /api/v1/files/{id}.
        var text = link?.ToJsonString().Trim('"') ?? "";
        if (!Guid.TryParse(text[(text.LastIndexOf('/') + 1)..], out var file))
        {
            return new Letterhead(name);
        }

        try
        {
            return await files.ReadAsync(file, ct).ConfigureAwait(false) is { } read && read.File.ContentType is "image/png" or "image/jpeg"
                ? new Letterhead(name, read.Content) : new Letterhead(name);
        }
        catch (InvalidOperationException)
        {
            return new Letterhead(name);
        }
    }

    // A file name of letters, digits and dashes only (the document's own code can hold anything).
    private static string FileNamePart(string text) =>
        new string([.. text.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-') is { Length: > 0 } name ? name : "document";
}

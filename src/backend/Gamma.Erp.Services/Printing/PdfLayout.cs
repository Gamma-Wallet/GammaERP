using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Gamma.Erp.Services.Printing;

/// <summary>The fonts the PDFs use, from the assembly (the same on every host): DejaVu Sans, with ë ç, Greek and Cyrillic.</summary>
internal sealed class EmbeddedFonts : IFontResolver
{
    public const string Family = "DejaVu Sans";

    private static readonly Lazy<bool> Installed = new(() =>
    {
        GlobalFontSettings.FontResolver = new EmbeddedFonts();
        return true;
    });

    /// <summary>Sets the resolver once for the process, before the first PDF.</summary>
    public static void Install() => _ = Installed.Value;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "DejaVuSans-Bold" : "DejaVuSans");

    public byte[]? GetFont(string faceName)
    {
        using var stream = typeof(EmbeddedFonts).Assembly.GetManifestResourceStream($"Fonts.{faceName}.ttf");
        if (stream is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}

/// <summary>
/// The page every printout shares (A4, a heading, a table, page numbers) and the words on it in the reader's language: English, and the
/// packs' first languages (Albanian, Italian, Greek); any other language prints in English until its words are added.
/// </summary>
internal static class PdfLayout
{
    private static readonly Dictionary<string, Dictionary<string, string>> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new()
        {
            ["date"] = "Date", ["from"] = "From", ["to"] = "To", ["description"] = "Description", ["quantity"] = "Quantity", ["unit"] = "Unit", ["price"] = "Price",
            ["value"] = "Value", ["vat"] = "VAT", ["total"] = "Total", ["notPosted"] = "Not posted", ["page"] = "Page", ["account"] = "Account", ["name"] = "Name",
            ["opening"] = "Opening", ["debit"] = "Debit", ["credit"] = "Credit", ["closing"] = "Closing", ["document"] = "Document", ["balance"] = "Balance",
            ["partner"] = "Partner", ["base"] = "Base", ["period"] = "Period", ["trialBalance"] = "Trial balance", ["ledger"] = "Account ledger",
            ["vatRegister"] = "VAT register", ["vatReturn"] = "VAT return", ["output"] = "Output VAT (sales)", ["input"] = "Input VAT (purchases)",
            ["payable"] = "VAT payable", ["refundable"] = "VAT to be refunded", ["vatGroup"] = "VAT group", ["noVat"] = "No VAT", ["reverses"] = "Reverses",
            // Reports printed since C3 phase 6.
            ["stock"] = "Stock", ["itemTracking"] = "Item tracking", ["lines"] = "Sales and purchase lines", ["partnerLedger"] = "Partner ledger", ["costCenterLedger"] = "Cost centre ledger", ["partnerStatistics"] = "Partners", ["costCenter"] = "Cost centre", ["item"] = "Item", ["unitCost"] = "Unit cost", ["documents"] = "Documents", ["product"] = "Product", ["documentType"] = "Document type", ["category"] = "Category",
        },
        ["sq"] = new()
        {
            ["date"] = "Data", ["from"] = "Nga", ["to"] = "Për", ["description"] = "Përshkrimi", ["quantity"] = "Sasia", ["unit"] = "Njësia", ["price"] = "Çmimi",
            ["value"] = "Vlera", ["vat"] = "TVSH", ["total"] = "Totali", ["notPosted"] = "E pakontabilizuar", ["page"] = "Faqja", ["account"] = "Llogaria",
            ["name"] = "Emërtimi", ["opening"] = "Fillimi", ["debit"] = "Debi", ["credit"] = "Kredi", ["closing"] = "Fundi", ["document"] = "Dokumenti",
            ["balance"] = "Gjendja", ["partner"] = "Partneri", ["base"] = "Baza", ["period"] = "Periudha", ["trialBalance"] = "Bilanci i verifikimit",
            ["ledger"] = "Kartela e llogarisë", ["vatRegister"] = "Libri i TVSH-së", ["vatReturn"] = "Deklarata e TVSH-së", ["output"] = "TVSH e shitjeve",
            ["input"] = "TVSH e blerjeve", ["payable"] = "TVSH për t'u paguar", ["refundable"] = "TVSH për t'u rimbursuar", ["vatGroup"] = "Grupi i TVSH-së",
            ["noVat"] = "Pa TVSH", ["reverses"] = "Stornim i",
            // Reports printed since C3 phase 6.
            ["stock"] = "Gjendja e magazinës", ["itemTracking"] = "Gjurmimi i artikullit", ["lines"] = "Rreshtat e shitjeve dhe blerjeve", ["partnerLedger"] = "Kartela e partnerit", ["costCenterLedger"] = "Kartela e qendrës së kostos", ["partnerStatistics"] = "Partnerët", ["costCenter"] = "Qendra e kostos", ["item"] = "Artikulli", ["unitCost"] = "Kosto për njësi", ["documents"] = "Dokumente", ["product"] = "Produkti", ["documentType"] = "Lloji i dokumentit", ["category"] = "Kategoria",
        },
        ["it"] = new()
        {
            ["date"] = "Data", ["from"] = "Da", ["to"] = "A", ["description"] = "Descrizione", ["quantity"] = "Quantità", ["unit"] = "Unità", ["price"] = "Prezzo",
            ["value"] = "Imponibile", ["vat"] = "IVA", ["total"] = "Totale", ["notPosted"] = "Non contabilizzato", ["page"] = "Pagina", ["account"] = "Conto",
            ["name"] = "Descrizione", ["opening"] = "Saldo iniziale", ["debit"] = "Dare", ["credit"] = "Avere", ["closing"] = "Saldo finale",
            ["document"] = "Documento", ["balance"] = "Saldo", ["partner"] = "Controparte", ["base"] = "Imponibile", ["period"] = "Periodo",
            ["trialBalance"] = "Bilancio di verifica", ["ledger"] = "Mastrino", ["vatRegister"] = "Registro IVA", ["vatReturn"] = "Liquidazione IVA",
            ["output"] = "IVA a debito (vendite)", ["input"] = "IVA a credito (acquisti)", ["payable"] = "IVA da versare", ["refundable"] = "IVA a credito",
            ["vatGroup"] = "Aliquota", ["noVat"] = "Senza IVA", ["reverses"] = "Storno di",
            // Reports printed since C3 phase 6.
            ["stock"] = "Giacenze", ["itemTracking"] = "Tracciabilità articolo", ["lines"] = "Righe di vendite e acquisti", ["partnerLedger"] = "Scheda partner", ["costCenterLedger"] = "Scheda centro di costo", ["partnerStatistics"] = "Partner", ["costCenter"] = "Centro di costo", ["item"] = "Articolo", ["unitCost"] = "Costo unitario", ["documents"] = "Documenti", ["product"] = "Prodotto", ["documentType"] = "Tipo di documento", ["category"] = "Categoria",
        },
        ["el"] = new()
        {
            ["date"] = "Ημερομηνία", ["from"] = "Από", ["to"] = "Προς", ["description"] = "Περιγραφή", ["quantity"] = "Ποσότητα", ["unit"] = "Μονάδα",
            ["price"] = "Τιμή", ["value"] = "Αξία", ["vat"] = "ΦΠΑ", ["total"] = "Σύνολο", ["notPosted"] = "Μη καταχωρημένο", ["page"] = "Σελίδα",
            ["account"] = "Λογαριασμός", ["name"] = "Περιγραφή", ["opening"] = "Από μεταφορά", ["debit"] = "Χρέωση", ["credit"] = "Πίστωση",
            ["closing"] = "Υπόλοιπο", ["document"] = "Παραστατικό", ["balance"] = "Υπόλοιπο", ["partner"] = "Συναλλασσόμενος", ["base"] = "Καθαρή αξία",
            ["period"] = "Περίοδος", ["trialBalance"] = "Ισοζύγιο", ["ledger"] = "Καρτέλα λογαριασμού", ["vatRegister"] = "Βιβλίο ΦΠΑ",
            ["vatReturn"] = "Δήλωση ΦΠΑ", ["output"] = "ΦΠΑ εκροών", ["input"] = "ΦΠΑ εισροών", ["payable"] = "ΦΠΑ για καταβολή",
            ["refundable"] = "ΦΠΑ προς επιστροφή", ["vatGroup"] = "Συντελεστής", ["noVat"] = "Χωρίς ΦΠΑ", ["reverses"] = "Αντιλογισμός του",
            // Reports printed since C3 phase 6.
            ["stock"] = "Απόθεμα", ["itemTracking"] = "Ιχνηλασία είδους", ["lines"] = "Γραμμές πωλήσεων και αγορών", ["partnerLedger"] = "Καρτέλα συναλλασσομένου", ["costCenterLedger"] = "Καρτέλα κέντρου κόστους", ["partnerStatistics"] = "Συναλλασσόμενοι", ["costCenter"] = "Κέντρο κόστους", ["item"] = "Είδος", ["unitCost"] = "Κόστος μονάδας", ["documents"] = "Παραστατικά", ["product"] = "Προϊόν", ["documentType"] = "Τύπος παραστατικού", ["category"] = "Κατηγορία",
        },
    };

    public static string Word(string language, string key) =>
        Words.TryGetValue(language, out var words) && words.TryGetValue(key, out var word) ? word : Words["en"][key];

    public static CultureInfo Culture(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public static string Amount(decimal? value, CultureInfo culture) => (value ?? 0).ToString("N2", culture);

    public static string Day(DateOnly? date, CultureInfo culture) => date?.ToString("d", culture) ?? "";

    /// <summary>
    /// What heads every print: the company's name, its logo when it has one (an image, PNG or JPEG), and the details it has filled in
    /// (trade name, tax number, address, phone, e-mail), each a line "Label: value": the same places on every print, the value when it is set.
    /// </summary>
    public sealed record Letterhead(string Name, byte[]? Logo = null, IReadOnlyList<string>? Details = null);

    /// <summary>An A4 page with the company's logo and name above the title, and the page number in the footer.</summary>
    public static (Document Document, Section Section) Page(Letterhead company, string title, string? subtitle, string language, bool landscape = false)
    {
        EmbeddedFonts.Install();
        var document = new Document();
        document.Info.Title = title;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFonts.Family;
        normal.Font.Size = 9;
        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        // A4 by its sizes (setting the orientation alone leaves the page upright in MigraDoc).
        (section.PageSetup.PageWidth, section.PageSetup.PageHeight) = landscape
            ? (Unit.FromCentimeter(29.7), Unit.FromCentimeter(21)) : (Unit.FromCentimeter(21), Unit.FromCentimeter(29.7));
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

        if (company.Logo is { Length: > 0 } logo)
        {
            var image = section.AddParagraph().AddImage("base64:" + Convert.ToBase64String(logo));
            image.Height = Unit.FromCentimeter(1.6);
            image.LockAspectRatio = true;
        }

        section.AddParagraph(company.Name).Format.Font.Size = 10;
        foreach (var line in company.Details ?? [])
        {
            var detail = section.AddParagraph(line);
            detail.Format.Font.Size = 8;
            detail.Format.Font.Color = Colors.DimGray;
        }
        var heading = section.AddParagraph(title);
        heading.Format.Font.Size = 15;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = Unit.FromPoint(4);
        if (subtitle is not null)
        {
            section.AddParagraph(subtitle).Format.SpaceAfter = Unit.FromPoint(8);
        }

        // Powered by Gamma Wallet on the left, the page number on the right.
        var footer = section.Footers.Primary.AddParagraph("Powered by Gamma Wallet · ");
        footer.AddHyperlink("https://www.gamma-wallet.com", HyperlinkType.Web).AddText("www.gamma-wallet.com");
        footer.Format.TabStops.AddTabStop(section.PageSetup.PageWidth - section.PageSetup.LeftMargin - section.PageSetup.RightMargin, TabAlignment.Right);
        footer.AddTab();
        footer.AddText($"{Word(language, "page")} ");
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();
        footer.Format.Font.Size = 7;
        return (document, section);
    }

    /// <summary>A table with a repeated header row; numbers right-aligned; a bold row for groups and totals.</summary>
    public static void Table(Section section, IReadOnlyList<(string Header, double Centimetres, bool Number)> columns, IEnumerable<(IReadOnlyList<string> Cells, bool Bold)> rows)
    {
        var table = section.AddTable();
        table.Borders.Bottom.Width = 0.25;
        table.Format.SpaceBefore = Unit.FromPoint(1);
        table.Format.SpaceAfter = Unit.FromPoint(1);
        foreach (var column in columns)
        {
            table.AddColumn(Unit.FromCentimeter(column.Centimetres)).Format.Alignment = column.Number ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Shading.Color = Colors.WhiteSmoke;
        for (var i = 0; i < columns.Count; i++)
        {
            header.Cells[i].AddParagraph(columns[i].Header);
        }

        foreach (var (cells, bold) in rows)
        {
            var row = table.AddRow();
            row.Format.Font.Bold = bold;
            for (var i = 0; i < columns.Count && i < cells.Count; i++)
            {
                row.Cells[i].AddParagraph(cells[i]);
            }
        }
    }

    public static byte[] Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, false);
        return output.ToArray();
    }
}

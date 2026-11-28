using System.Globalization;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Printing;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>An invoice on the network as the screens show it.</summary>
public sealed record EInvoiceDto(Guid Id, string Direction, Guid? DocumentId, string? DocumentCode, string? ExternalId, string? ExternalNumber, string Status,
    string? PaymentStatus, decimal? Amount, string? PartnerName, string? PartnerVatNumber, Guid? PaymentId, string? Errors, DateTimeOffset? SentOn, DateTimeOffset? CheckedOn,
    DateTimeOffset? PaidOn);

/// <summary>A document's e-invoicing for its screen: may it be sent, through which network, and what was sent.</summary>
public sealed record EInvoiceStateDto(bool Enabled, string? Network, EInvoiceDto? Sent);

/// <summary>Why a received invoice is rejected (the supplier reads it).</summary>
public sealed record EInvoiceRejectRequest(string? Reason);

/// <summary>
/// B2B e-invoices through the network (Jinius, the founder 2026-11-28). Every step is a person's press: Send a posted sales invoice, Check it (when the network
/// shows it paid, the payment is booked against that invoice, in full, as a payment line naming it), list the invoices suppliers sent, Approve or Reject one,
/// and Enter one as a draft purchase invoice. The network's codes, the properties holding the tax numbers and the documents used come from the provider's
/// codes; nothing about VAT, units or accounts is decided here.
/// </summary>
public sealed class EInvoices(ErpDbContext db, Visibility visibility, ISecretBox secrets, JiniusClient jinius, DocumentService documents,
    OpenItems openItems, PrintService prints, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>
    /// For the document screen: whether the document may go to the network (the company has a connection and the codes allow its type), the network's
    /// name, and what was sent and last heard of it.
    /// </summary>
    public async Task<EInvoiceStateDto> OfDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var bill = await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.Active, ct).ConfigureAwait(false);
        if (bill is null || !nodes.Contains(bill.CompanyId) && (bill.OwnerId is not { } owner || !nodes.Contains(owner)))
        {
            return new EInvoiceStateDto(false, null, null);
        }

        var provider = await db.Set<EInvoiceProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == bill.CompanyId, ct).ConfigureAwait(false);
        var allowed = provider is not null && (provider.Codes?["documentTypes"] is not JsonArray { Count: > 0 } types
            || types.Any(t => Guid.TryParse((string?)t, out var id) && id == bill.TypeId));
        var link = await db.Set<EInvoiceLink>().AsNoTracking().Where(l => l.Active && l.DocumentId == documentId && l.Direction == "Sent").OrderByDescending(l => l.CreatedOn)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new EInvoiceStateDto(allowed, provider?.Kind, link is null ? null : await DtoAsync(link, ct).ConfigureAwait(false));
    }

    /// <summary>Send (a person's press): the posted sales invoice goes to the network as a structured e-invoice, with its PDF.</summary>
    public async Task<EInvoiceDto> SendAsync(Guid documentId, CancellationToken ct)
    {
        var (bill, provider) = await DocumentAndProviderAsync(documentId, ct).ConfigureAwait(false);
        var link = await db.Set<EInvoiceLink>().FirstOrDefaultAsync(l => l.Active && l.DocumentId == documentId && l.Direction == "Sent", ct).ConfigureAwait(false);
        if (link is { ExternalId: { Length: > 0 } })
        {
            return await DtoAsync(link, ct).ConfigureAwait(false);
        }

        if (link is null)
        {
            link = new EInvoiceLink { TenantId = bill.TenantId, OwnerId = bill.CompanyId, EInvoiceProviderId = provider.Id, DocumentId = bill.Id, Direction = "Sent" };
            db.Add(link);
        }

        (link.Attempts, link.SentOn, link.Errors) = (link.Attempts + 1, time.GetUtcNow(), null);
        var (body, problem) = await InvoiceAsync(bill, provider, ct).ConfigureAwait(false);
        if (body is null)
        {
            (link.Status, link.Errors) = ("Refused", problem);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return await DtoAsync(link, ct).ConfigureAwait(false);
        }

        (link.ExternalNumber, link.Amount, link.PartnerName, link.PartnerVatNumber) =
            ((string?)body["documentNumber"], (decimal?)body["totalAmount"], bill.ToName ?? bill.FromName, (string?)body["recipientVatNumber"]);
        var secret = Secret(provider);
        try
        {
            var answer = await jinius.CallAsync(provider, secret, HttpMethod.Post, "/v3/invoices", body, ct).ConfigureAwait(false);
            link.Answer = answer.Body;
            if (!answer.Ok || (string?)answer.Body?["id"] is not { Length: > 0 } id)
            {
                (link.Status, link.Errors) = ("Refused", answer.Problem);
            }
            else
            {
                (link.ExternalId, link.Status) = (id, (string?)answer.Body?["status"] ?? "Sent");
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                link.Errors = await AttachPdfAsync(provider, secret, id, bill, ct).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            (link.Status, link.Errors) = (link.ExternalId is null ? "Refused" : link.Status, e.Message);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await DtoAsync(link, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Check (a person's press): the network's state of a sent invoice. When it shows the invoice paid and the invoice is still open in the ERP, the payment is
    /// booked: a document of the codes' payment type from the partner into the codes' bank, one line naming the invoice for all that is open on it.
    /// </summary>
    public async Task<EInvoiceDto> CheckAsync(Guid documentId, CancellationToken ct)
    {
        var (bill, provider) = await DocumentAndProviderAsync(documentId, ct).ConfigureAwait(false);
        var link = await db.Set<EInvoiceLink>().FirstOrDefaultAsync(l => l.Active && l.DocumentId == documentId && l.Direction == "Sent" && l.ExternalId != null, ct)
            .ConfigureAwait(false) ?? throw ValidationException.For("documentId", "The invoice was not sent to the network yet: press Send.");
        link.CheckedOn = time.GetUtcNow();
        try
        {
            var answer = await jinius.CallAsync(provider, Secret(provider), HttpMethod.Get, $"/v3/invoices/{Uri.EscapeDataString(link.ExternalId!)}", null, ct)
                .ConfigureAwait(false);
            if (!answer.Ok)
            {
                link.Errors = answer.Problem;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return await DtoAsync(link, ct).ConfigureAwait(false);
            }

            link.Answer = answer.Body;
            (link.Status, link.Errors) = ((string?)answer.Body?["status"] ?? link.Status, null);
            var (paid, paymentStatus, reference) = Paid(answer.Body, link.Amount ?? (decimal?)answer.Body?["totalAmount"] ?? 0m);
            link.PaymentStatus = paymentStatus ?? link.PaymentStatus;
            if (paid && link.PaymentId is null)
            {
                link.PaidOn ??= time.GetUtcNow();
                try
                {
                    link.PaymentId = await BookPaymentAsync(bill, provider, reference, ct).ConfigureAwait(false);
                }
                catch (ValidationException e)
                {
                    link.Errors = string.Join(" ", e.Errors.Values);
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            link.Errors = e.Message;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await DtoAsync(link, ct).ConfigureAwait(false);
    }

    /// <summary>The invoices suppliers sent the company on the network (its payable list), each kept as a Received row; nothing is entered in the books.</summary>
    public async Task<IReadOnlyList<EInvoiceDto>> ReceivedAsync(CancellationToken ct)
    {
        var provider = await ProviderAsync(await CompanyAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        var answer = await jinius.CallAsync(provider, Secret(provider), HttpMethod.Get, "/v3/invoices/payable?page=1&pageSize=100", null, ct).ConfigureAwait(false);
        if (!answer.Ok)
        {
            throw ValidationException.For("network", $"The network did not list the invoices: {answer.Problem}");
        }

        var kept = await db.Set<EInvoiceLink>().Where(l => l.Active && l.EInvoiceProviderId == provider.Id && l.Direction == "Received").ToListAsync(ct).ConfigureAwait(false);
        var list = new List<EInvoiceLink>();
        foreach (var invoice in (answer.Body?["documents"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var id = (string?)invoice["id"];
            if (id is null)
            {
                continue;
            }

            var row = kept.FirstOrDefault(l => l.ExternalId == id);
            if (row is null)
            {
                row = new EInvoiceLink { TenantId = provider.TenantId, OwnerId = provider.OwnerId, EInvoiceProviderId = provider.Id, Direction = "Received", ExternalId = id };
                db.Add(row);
            }

            (row.ExternalNumber, row.Status, row.Amount, row.Answer) = ((string?)invoice["documentNumber"], (string?)invoice["status"] ?? row.Status,
                (decimal?)invoice["totalAmount"], invoice.DeepClone());
            (row.PartnerName, row.PartnerVatNumber) = ((string?)invoice["issuerLegalEntityDetails"]?["registeredBusinessName"] ?? row.PartnerName,
                (string?)invoice["issuerVatNumber"]);
            row.PaymentStatus = Paid(invoice, row.Amount ?? 0m).Status ?? row.PaymentStatus;
            row.CheckedOn = time.GetUtcNow();
            list.Add(row);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var dtos = new List<EInvoiceDto>();
        foreach (var row in list)
        {
            dtos.Add(await DtoAsync(row, ct).ConfigureAwait(false));
        }

        return dtos;
    }

    /// <summary>Approve (a person's press) an invoice a supplier sent.</summary>
    public Task<EInvoiceDto> ApproveAsync(Guid linkId, CancellationToken ct) => ActAsync(linkId, "approve", null, ct);

    /// <summary>Reject (a person's press) an invoice a supplier sent, with the reason the supplier reads.</summary>
    public Task<EInvoiceDto> RejectAsync(Guid linkId, string? reason, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(reason) ? throw ValidationException.For("reason", "Say why the invoice is rejected; the supplier reads it.")
            : ActAsync(linkId, "reject", new JsonObject { ["rejectionReason"] = reason.Trim() }, ct);

    /// <summary>
    /// Enter (a person's press): a received invoice becomes a draft purchase invoice of the codes' type (codes.received), from the supplier whose VAT number the
    /// partner property holds, one line per network line with the codes' line type and element and the VAT group the codes give the network's tax code. It is not
    /// posted: the person checks and posts it.
    /// </summary>
    public async Task<EInvoiceDto> EnterAsync(Guid linkId, CancellationToken ct)
    {
        var link = await ReceivedLinkAsync(linkId, ct).ConfigureAwait(false);
        if (link.DocumentId is not null)
        {
            return await DtoAsync(link, ct).ConfigureAwait(false);
        }

        var provider = await db.Set<EInvoiceProvider>().AsNoTracking().FirstAsync(p => p.Id == link.EInvoiceProviderId, ct).ConfigureAwait(false);
        var codes = provider.Codes?["received"] as JsonObject
            ?? throw ValidationException.For("codes", $"{ErrorCodes.NotConfiguredError}: the e-invoicing codes don't say how a received invoice is entered (codes.received).");
        Guid Id(string key) => Guid.TryParse((string?)codes[key], out var id) ? id
            : throw ValidationException.For($"codes.received.{key}", $"{ErrorCodes.NotConfiguredError}: codes.received.{key} is missing.");
        var invoice = link.Answer as JsonObject ?? throw ValidationException.For("linkId", "List the received invoices again first.");
        var supplierVat = (string?)invoice["issuerVatNumber"];
        var supplier = await PartnerByVatAsync(provider, supplierVat, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("supplier", $"No partner has the VAT number {supplierVat}: add the supplier first (its VAT number in the property the codes name).");
        var toId = Id("toId");
        var to = await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == toId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("codes.received.toId", "There is no such cost centre.");
        var vatGroups = codes["vat"] as JsonObject;
        var lines = new List<TransactionDto>();
        foreach (var line in (invoice["lineItems"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var tax = line["taxPercentage"]?.ToString() ?? "None";
            if (!Guid.TryParse((string?)vatGroups?[tax], out var vatGroup))
            {
                throw ValidationException.For("codes.received.vat", $"{ErrorCodes.NotConfiguredError}: no VAT group for the network's tax code {tax} (codes.received.vat).");
            }

            lines.Add(new TransactionDto
            {
                TypeId = Id("transactionTypeId"), TypeConfId = Id("typeConfId"), ElementId = Id("elementId"), ElementName = (string?)line["description"],
                Quantity = (decimal?)line["quantity"] ?? 1, Price = (decimal?)line["price"] ?? 0, Discount = (decimal?)line["discount"], VatGroupId = vatGroup,
            });
        }

        if (lines.Count == 0)
        {
            throw ValidationException.For("linkId", "The received invoice has no lines.");
        }

        var day = Day((string?)invoice["issueDate"]) ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = Id("documentTypeId"), DocumentDate = day, PostingDate = day, DueDate = Day((string?)invoice["dueDate"]), FromId = supplier.Id,
            FromName = supplier.Name.In(TranslatedText.DefaultLanguage), ToId = to.Id, ToName = to.Name.In(TranslatedText.DefaultLanguage), ExternalNumber = link.ExternalNumber,
            Transactions = lines,
        }, ct).ConfigureAwait(false);
        (link.DocumentId, link.Errors) = (saved.Id, null);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await DtoAsync(link, ct).ConfigureAwait(false);
    }

    // ---- sending ----

    /// <summary>The network's invoice from a posted sales invoice, or why it can't be made.</summary>
    private async Task<(JsonObject? Body, string? Problem)> InvoiceAsync(Document bill, EInvoiceProvider provider, CancellationToken ct)
    {
        var partnerId = await db.Set<Partner>().AsNoTracking().Where(p => p.Id == bill.ToId || p.Id == bill.FromId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (partnerId is null)
        {
            return (null, "The invoice names no client partner.");
        }

        var issuer = await EInvoiceProviders.CompanyIdsAsync(db, provider, bill.CompanyId, ct).ConfigureAwait(false);
        var recipient = await EInvoiceProviders.PartnerIdsAsync(db, provider, partnerId.Value, ct).ConfigureAwait(false);
        if (issuer.Count < 2)
        {
            return (null, $"{ErrorCodes.NotConfiguredError}: the company's properties hold {issuer.Count} of VAT number, tax id and registration number; the network needs two (codes.company).");
        }

        if (recipient.Count < 2)
        {
            return (null, $"The client's properties hold {recipient.Count} of VAT number, tax id and registration number; the network needs two (codes.partner).");
        }

        var lines = await db.Set<Transaction>().AsNoTracking().Where(t => t.Active && t.DocumentId == bill.Id && t.VatGroupId != null && t.ValueLcu != null)
            .OrderBy(t => t.CreatedOn).ToListAsync(ct).ConfigureAwait(false);
        var vatCodes = provider.Codes?["vat"] as JsonObject;
        var unitCodes = provider.Codes?["unit"] as JsonObject;
        var defaultUnit = (string?)provider.Codes?["defaultUnit"];
        var items = new JsonArray();
        decimal net = 0, vat = 0, discount = 0, initial = 0;
        foreach (var line in lines)
        {
            if ((string?)vatCodes?[line.VatGroupId.ToString()!] is not { } tax)
            {
                return (null, $"{ErrorCodes.NotConfiguredError}: the VAT group of line {line.ElementName} has no network code (codes.vat).");
            }

            var unit = line.UnitId is { } u ? (string?)unitCodes?[u.ToString()] ?? defaultUnit : defaultUnit;
            if (unit is null)
            {
                return (null, $"{ErrorCodes.NotConfiguredError}: the unit of line {line.ElementName} has no network code (codes.unit or codes.defaultUnit).");
            }

            var rate = line.ExchangeRate is > 0 ? line.ExchangeRate.Value : 1m;
            var quantity = line.Quantity ?? 1;
            var price = Money((line.Price ?? 0) * rate);
            var lineDiscount = Money(line.DiscountLcu ?? (line.Discount ?? 0) * rate);
            var lineNet = Money(line.ValueLcu ?? 0);
            var lineVat = Money(line.VatLcu ?? 0);
            (net, vat, discount, initial) = (net + lineNet, vat + lineVat, discount + lineDiscount, initial + Money(price * quantity));
            items.Add(new JsonObject
            {
                ["description"] = line.ElementName ?? "Item", ["quantity"] = quantity, ["unit"] = unit, ["price"] = price, ["discount"] = lineDiscount,
                ["taxPercentage"] = tax, ["lineTotal"] = Money(lineNet + lineVat), ["taxAmount"] = lineVat,
            });
        }

        if (items.Count == 0)
        {
            return (null, "The invoice has no lines to send.");
        }

        var day = bill.DocumentDate ?? bill.PostingDate ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var due = bill.DueDate ?? day.AddDays((int?)provider.Codes?["dueDays"] ?? 30);
        var number = new string([.. (bill.Code ?? bill.Id.ToString("N")).Where(char.IsAsciiLetterOrDigit)]);
        var body = new JsonObject
        {
            ["documentNumber"] = number, ["currency"] = await CurrencyAsync(bill.CompanyId, ct).ConfigureAwait(false), ["issueDate"] = Iso(day), ["dueDate"] = Iso(due),
            ["description"] = bill.Description?.In(TranslatedText.DefaultLanguage) ?? "",
            ["net"] = Money(net), ["vatAmount"] = Money(vat), ["totalAmount"] = Money(net + vat), ["discount"] = Money(discount), ["initial"] = Money(initial),
            ["lineItems"] = items,
        };
        Put(body, "issuerVatNumber", issuer.Vat);
        Put(body, "issuerTaxIdNumber", issuer.Tin);
        Put(body, "issuerCompanyRegistrationNumber", issuer.Registration);
        Put(body, "recipientVatNumber", recipient.Vat);
        Put(body, "recipientTaxIdNumber", recipient.Tin);
        Put(body, "recipientCompanyRegistrationNumber", recipient.Registration);
        return (body, null);
    }

    private async Task<string?> AttachPdfAsync(EInvoiceProvider provider, string secret, string id, Document bill, CancellationToken ct)
    {
        try
        {
            if (await prints.DocumentAsync(bill.Id, TranslatedText.DefaultLanguage, ct).ConfigureAwait(false) is not { } pdf)
            {
                return "Sent; the invoice's PDF could not be made, so none is attached.";
            }

            var answer = await jinius.AttachPdfAsync(provider, secret, id, pdf.Content, pdf.FileName, ct).ConfigureAwait(false);
            return answer.Ok ? null : $"Sent; the PDF was not attached: {answer.Problem}";
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException or ValidationException)
        {
            return $"Sent; the PDF was not attached: {e.Message}";
        }
    }

    // ---- paying ----

    /// <summary>Whether the network shows the invoice paid: its status, or completed payments covering the total; and the latest payment's state and reference.</summary>
    private static (bool Paid, string? Status, string? Reference) Paid(JsonNode? invoice, decimal total)
    {
        var payments = (invoice?["payments"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var latest = payments.LastOrDefault();
        var completed = payments.Where(p => string.Equals((string?)p["status"], "Completed", StringComparison.OrdinalIgnoreCase)).Sum(p => (decimal?)p["amount"] ?? 0m);
        var status = (string?)invoice?["status"];
        var paid = string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase) || (total > 0 && completed >= total - 0.01m);
        return (paid, (string?)latest?["status"], (string?)latest?["transactionId"] ?? (string?)latest?["referenceNumber"] ?? (string?)latest?["id"]);
    }

    /// <summary>The payment of a paid invoice: what is open on it, from the partner into the codes' bank, one line naming the invoice (checked in full by the open items).</summary>
    private async Task<Guid?> BookPaymentAsync(Document bill, EInvoiceProvider provider, string? reference, CancellationToken ct)
    {
        var codes = provider.Codes?["payment"] as JsonObject;
        if (!Guid.TryParse((string?)codes?["documentTypeId"], out var paymentType) || !Guid.TryParse((string?)codes?["bankId"], out var bankId))
        {
            throw ValidationException.For("codes.payment", $"{ErrorCodes.NotConfiguredError}: the e-invoicing codes don't name the payment document type and bank (codes.payment).");
        }

        var partner = await db.Set<Partner>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == bill.ToId || p.Id == bill.FromId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("documentId", "The invoice names no partner.");
        var day = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var open = (await openItems.ListAsync(day, partner.Id, ct).ConfigureAwait(false)).FirstOrDefault(i => i.Id == bill.Id);
        if (open is null || open.Open <= 0)
        {
            throw ValidationException.For("documentId", "Paid on the network; the invoice is already paid in the ERP, so no payment was booked.");
        }

        var bank = await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == bankId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("codes.payment.bankId", "There is no such bank.");
        var lineType = await (from l in db.Set<InterDocumentTypeTransactionType>().AsNoTracking()
                              join c in db.Set<TransactionTypeConf>().AsNoTracking() on l.TransactionTypeId equals c.TransactionTypeId
                              join e in db.Set<Models.Platform.Elements>().AsNoTracking() on c.ElementsId equals e.Id
                              where l.DocumentTypeId == paymentType && e.TypeId == bank.TypeId && l.TransactionTypeId != null
                              select new { l.TransactionTypeId, Conf = c.Id }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("codes.payment.documentTypeId", $"{ErrorCodes.NotConfiguredError}: the payment type has no line for the bank's type.");
        var zero = await (from l in db.Set<InterDocumentTypeVatGroup>().AsNoTracking() join v in db.Set<VatGroup>().AsNoTracking() on l.VatGroupId equals v.Id
                          where l.DocumentTypeId == paymentType && v.Rate == 0 select (Guid?)v.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var category = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == paymentType).Select(l => l.CategoryId).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = paymentType, CategoryIds = category == Guid.Empty ? [] : [category], DocumentDate = day, PostingDate = day, FromId = partner.Id,
            FromName = partner.Name.In(TranslatedText.DefaultLanguage), ToId = bank.Id, ToName = bank.Name.In(TranslatedText.DefaultLanguage), ExternalNumber = reference,
            CreatedFromIds = [bill.Id],
            Transactions =
            [
                new TransactionDto
                {
                    TypeId = lineType.TransactionTypeId!.Value, TypeConfId = lineType.Conf, ElementId = bank.Id, ElementTypeId = bank.TypeId, ElementName = bill.Code, Price = open.Open,
                    Quantity = 1, VatGroupId = zero, ReferenceNumber = bill.Code,
                },
            ],
        }, ct).ConfigureAwait(false);
        var posted = await documents.PostAsync(saved.Id!.Value, ct).ConfigureAwait(false);
        return posted.Id;
    }

    // ---- received ----

    private async Task<EInvoiceDto> ActAsync(Guid linkId, string action, JsonNode? body, CancellationToken ct)
    {
        var link = await ReceivedLinkAsync(linkId, ct).ConfigureAwait(false);
        var provider = await db.Set<EInvoiceProvider>().AsNoTracking().FirstAsync(p => p.Id == link.EInvoiceProviderId, ct).ConfigureAwait(false);
        var answer = await jinius.CallAsync(provider, Secret(provider), HttpMethod.Post, $"/v3/invoices/{Uri.EscapeDataString(link.ExternalId!)}/{action}", body, ct)
            .ConfigureAwait(false);
        if (answer.Ok)
        {
            (link.Status, link.Answer, link.Errors) = ((string?)answer.Body?["status"] ?? link.Status, answer.Body ?? link.Answer, null);
        }
        else
        {
            link.Errors = answer.Problem;
        }

        link.CheckedOn = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await DtoAsync(link, ct).ConfigureAwait(false);
    }

    private async Task<EInvoiceLink> ReceivedLinkAsync(Guid linkId, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var link = await db.Set<EInvoiceLink>().FirstOrDefaultAsync(l => l.Active && l.Id == linkId && l.Direction == "Received", ct).ConfigureAwait(false);
        return link is null || link.OwnerId is not { } owner || !nodes.Contains(owner) || link.ExternalId is null
            ? throw ValidationException.For("linkId", "There is no such received invoice.")
            : link;
    }

    private async Task<Partner?> PartnerByVatAsync(EInvoiceProvider provider, string? vat, CancellationToken ct)
    {
        var code = (string?)provider.Codes?["partner"]?["vat"];
        if (string.IsNullOrWhiteSpace(vat) || code is null)
        {
            return null;
        }

        var wanted = vat.Replace(" ", "", StringComparison.Ordinal);
        var candidates = await (from v in db.Set<PartnerInfo>().AsNoTracking() join t in db.Set<PartnerTypeInfo>().AsNoTracking() on v.TypeInfoId equals t.Id
                                where v.Active && t.Active && t.Code == code select new { v.ElementId, v.Value }).ToListAsync(ct).ConfigureAwait(false);
        var ids = candidates.Where(c => EInvoiceProviders.Text(c.Value) is { } text
                && (string.Equals(text, wanted, StringComparison.OrdinalIgnoreCase) || text.EndsWith(wanted, StringComparison.OrdinalIgnoreCase)
                    || wanted.EndsWith(text, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.ElementId).ToList();
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        return (await db.Set<Partner>().AsNoTracking().Where(p => p.Active && ids.Contains(p.Id)).ToListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(p => p.OwnerId is null || nodes.Contains(p.OwnerId.Value));
    }

    // ---- common ----

    private async Task<(Document Bill, EInvoiceProvider Provider)> DocumentAndProviderAsync(Guid documentId, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var bill = await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.Active, ct).ConfigureAwait(false);
        if (bill is null || !nodes.Contains(bill.CompanyId) && (bill.OwnerId is not { } owner || !nodes.Contains(owner)))
        {
            throw ValidationException.For("documentId", "There is no such document.");
        }

        if (!bill.Posted)
        {
            throw ValidationException.For("documentId", $"{ErrorCodes.PostedError}: post the invoice before sending it.");
        }

        var provider = await ProviderAsync(bill.CompanyId, ct).ConfigureAwait(false);
        if (provider.Codes?["documentTypes"] is JsonArray types && types.Count > 0 && !types.Any(t => Guid.TryParse((string?)t, out var id) && id == bill.TypeId))
        {
            throw ValidationException.For("documentId", $"{ErrorCodes.NotConfiguredError}: documents of this type are not sent to the network (codes.documentTypes).");
        }

        return (bill, provider);
    }

    private async Task<EInvoiceProvider> ProviderAsync(Guid company, CancellationToken ct) =>
        await db.Set<EInvoiceProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false)
        ?? throw ValidationException.For("provider", $"{ErrorCodes.NotConfiguredError}: the company has no e-invoicing connection (Configuration › E-invoicing).");

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "E-invoicing belongs to a company: choose one of its nodes.");

    private async Task<string> CurrencyAsync(Guid company, CancellationToken ct) =>
        await db.Set<Models.Currencies.Currency>().AsNoTracking().Where(c => c.Active && c.IsBase && c.OwnerId == company).Select(c => c.Code).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? "EUR";

    private string Secret(EInvoiceProvider provider) =>
        secrets.Open(provider.ClientSecret) ?? throw ValidationException.For("clientSecret", "The client's secret can't be read here: enter it again.");

    private async Task<EInvoiceDto> DtoAsync(EInvoiceLink l, CancellationToken ct)
    {
        var code = l.DocumentId is { } id
            ? await db.Set<Document>().AsNoTracking().Where(d => d.Id == id).Select(d => d.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        return new EInvoiceDto(l.Id, l.Direction, l.DocumentId, code, l.ExternalId, l.ExternalNumber, l.Status, l.PaymentStatus, l.Amount, l.PartnerName, l.PartnerVatNumber,
            l.PaymentId, l.Errors, l.SentOn, l.CheckedOn, l.PaidOn);
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";

    private static DateOnly? Day(string? iso) =>
        iso is { Length: >= 10 } && DateOnly.TryParseExact(iso[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static void Put(JsonObject body, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            body[key] = value;
        }
    }
}

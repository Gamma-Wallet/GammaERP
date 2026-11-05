using System.ComponentModel;
using System.Text.Json;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Charts = Gamma.Erp.Models.Charts;
using CostCenters = Gamma.Erp.Models.CostCenters;
using Employees = Gamma.Erp.Models.Employees;
using Partners = Gamma.Erp.Models.Partners;
using Products = Gamma.Erp.Models.Products;
using Transactions = Gamma.Erp.Models.Transactions;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>A line of a draft as Claude gives it; what is left out is proposed as the document editor proposes it.</summary>
public sealed record DraftLine(
    [property: Description("The line's product or service, or account for an account line: its id or exact code.")] string? Element = null,
    [property: Description("The kind of line (a transaction type of the document type, see document_types): id, code or name. Left out: the only kind, or the kind that holds the product.")] string? Kind = null,
    [property: Description("Quantity.")] decimal? Quantity = null,
    [property: Description("Unit price without VAT, in the line's currency. Left out: the price the ERP proposes (sale price list or last purchase).")] decimal? Price = null,
    [property: Description("Discount per unit.")] decimal? Discount = null,
    [property: Description("Unit: id or code. Left out: the product's default unit.")] string? Unit = null,
    [property: Description("VAT group: id, code or name. Left out: the product's.")] string? VatGroup = null,
    [property: Description("Currency: id or code. Left out: the company's own (LCU).")] string? Currency = null,
    [property: Description("Exchange rate for a foreign currency; left out: the rate of the document's day.")] decimal? ExchangeRate = null,
    [property: Description("Only when the line's own place differs from the document's: the cost centre id it comes from.")] Guid? From = null,
    [property: Description("Only when the line's own place differs from the document's: the cost centre id it goes to.")] Guid? To = null,
    [property: Description("Lot, for a product that tracks lots.")] string? Lot = null,
    [property: Description("Serial numbers, one per unit, for a product that tracks them.")] IReadOnlyList<string>? Serials = null);

/// <summary>A row of a journal sheet as Claude gives it: one journal under the sheet, debit or credit, as the sheet's grid holds it.</summary>
public sealed record SheetRow(
    [property: Description("The account (or other element the row's kind holds, e.g. a partner): its id or exact code.")] string Account,
    [property: Description("Debit amount in LCU; leave out or 0 when the row is a credit.")] decimal? Debit = null,
    [property: Description("Credit amount in LCU; leave out or 0 when the row is a debit.")] decimal? Credit = null,
    [property: Description("The row's document type, one of the journal types under the sheet's type: id, code or name. Left out: the only one, or the previous row's.")] string? Type = null,
    [property: Description("The kind of line (a transaction type of the row's type): id, code or name; \"kind · holds\" when the kind holds several things. Left out: the only one.")] string? Kind = null,
    [property: Description("The place (cost centre) of the row, when its type has one: id or exact code. Left out: the previous row's.")] string? Place = null,
    [property: Description("Optional: the row's description.")] string? Description = null);

/// <summary>Documents through Claude: the same document service, rights and rules as the document screens.</summary>
[McpServerToolType]
public sealed class DocumentTools(DocumentService documents, ErpDbContext db, RequestContext request, Visibility visibility)
{
    // ---------- Reading ----------

    [McpServerTool(Name = "list_documents", Title = "List documents", ReadOnly = true)]
    [Description("Lists the documents of one category (e.g. sales invoices, purchases, offers, payments) that the user may see, newest first: id, number, type, from, to, dates, posted or draft, total in LCU. Filter by type, posting period, state or a search text. Use document_types to see the categories.")]
    public Task<object> ListDocumentsAsync(
        [Description("The document category: its code, name or id.")] string category,
        [Description("Optional: one document type (id, code or name).")] string? type = null,
        [Description("Optional: posting date from (yyyy-mm-dd).")] DateOnly? from = null,
        [Description("Optional: posting date to (yyyy-mm-dd).")] DateOnly? to = null,
        [Description("Optional: true only posted, false only drafts.")] bool? posted = null,
        [Description("Optional: part of the number, a partner's name or the external number.")] string? search = null,
        [Description("Page number, from 1.")] int? page = null, [Description("Rows per page: 25 if not given, at most 100.")] int? pageSize = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var categoryId = Registers.Category(await documents.Entities.CategoriesAsync(ct).ConfigureAwait(false), category, "document")!.Value;
            var types = await documents.Entities.TypesAsync(ct).ConfigureAwait(false);
            Guid? typeId = string.IsNullOrWhiteSpace(type) ? null : Registers.Type(types, type, "document").Id;
            var (p, size) = Registers.Paging(page, pageSize);
            var list = await documents.ListAsync(new DocumentQuery(categoryId, typeId, Search: string.IsNullOrWhiteSpace(search) ? null : search.Trim(), Posted: posted,
                PostedFrom: from, PostedTo: to, Page: p, PageSize: size), ct).ConfigureAwait(false);
            var names = types.Where(t => t.Id is not null).ToDictionary(t => t.Id!.Value, t => Registers.Name(t.Name));
            return new
            {
                list.Total, list.Page, list.PageSize,
                items = list.Items.Select(d => new
                {
                    d.Id, number = d.Code, type = names.GetValueOrDefault(d.TypeId), d.ExternalNumber, from = d.FromName, to = d.ToName, state = d.Posted ? "posted" : "draft",
                    d.PostingDate, d.DocumentDate, currency = d.CurrencyCode, valueLcu = d.Value, vatLcu = d.Vat, totalLcu = d.Total, reverses = d.ReversedDocumentCode,
                }),
            };
        });

    [McpServerTool(Name = "get_document", Title = "Read a document", ReadOnly = true)]
    [Description("Reads one document: header (type, number, dates, from, to, payment method, state), its lines (kind, product or account, unit, quantity, price, VAT, amounts), totals per currency with LCU, the documents it was made from and those made from it. For a posted document also the quantities still reversible per line.")]
    public Task<object> GetDocumentAsync([Description("The document's id (from list_documents).")] Guid id, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var document = await documents.DocumentAsync(id, ct).ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.");
            return await ShowAsync(document, true, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "document_types", Title = "Document types I may write", ReadOnly = true)]
    [Description("The document categories and the types the user may write in each. Give a category to also get each type's form basics: its from and to sides (what they pick: partner, warehouse, cash desk…), its kinds of lines with what they hold, payment methods, VAT groups and currencies, and its properties. Use it before save_draft_document.")]
    public Task<object> DocumentTypesAsync(
        [Description("Optional: one category (code, name or id): its types come with their forms.")] string? category = null,
        [Description("Optional: one type (id, code or name): only its form.")] string? type = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var categories = await documents.Entities.CategoriesAsync(ct).ConfigureAwait(false);
            var categoryId = Registers.Category(categories, category, "document");
            var allowed = await documents.AllowedTypesAsync(categoryId, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(type))
            {
                var one = Registers.Type(allowed, type, "document");
                return await FormOfAsync(one, ct).ConfigureAwait(false);
            }

            if (categoryId is null)
            {
                return categories.Where(c => c.Active && c.Id is not null).OrderBy(c => c.SortOrder).Select(c => new
                {
                    c.Id, c.Code, name = Registers.Name(c.Name),
                    types = allowed.Where(t => t.CategoryIds.Contains(c.Id!.Value)).Select(t => new { t.Id, t.Code, name = Registers.Name(t.Name) }),
                }).Where(c => c.types.Any()).ToList();
            }

            var forms = new List<object>();
            foreach (var t in allowed.Where(t => t.IsElement || t.CategoryIds.Count > 0).Take(15))
            {
                forms.Add(await FormOfAsync(t, ct).ConfigureAwait(false));
            }

            return forms;
        });

    [McpServerTool(Name = "next_documents", Title = "What a document can make", ReadOnly = true)]
    [Description("What a document can generate (its Generate menu): the document types its flows allow, each with the reason it can't be made now if so, and the documents already made from it. Use make_next_document to make one.")]
    public Task<object> NextDocumentsAsync([Description("The document's id.")] Guid id, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var flows = await documents.FlowsAsync(id, Registers.English, ct).ConfigureAwait(false);
            var made = await documents.MadeFromAsync(id, Registers.English, ct).ConfigureAwait(false);
            return new
            {
                canMake = flows.Select(f => new { typeId = f.TypeId, f.Code, f.Name, refused = f.Refused }),
                alreadyMade = made.Select(m => new { m.Id, number = m.Code, m.Type, state = m.Posted ? "posted" : "draft" }),
            };
        });

    // ---------- Changing ----------

    [McpServerTool(Name = "save_draft_document", Title = "Make or change a draft document", ReadOnly = false, Destructive = false)]
    [Description("Makes a new draft document, or changes a draft (with its id): type, dates, from and to sides, lines. What is left out of a line is proposed as the document editor proposes it (unit, price, VAT group, kind of line). Nothing is booked: a draft is posted only with post_document. First show the user the document as it will be saved (sides, lines, amounts) and get an explicit yes; then call with confirm=true. Returns the saved draft.")]
    public Task<object> SaveDraftDocumentAsync(
        [Description("True only after the user said yes to this exact document.")] bool confirm,
        [Description("The document type (id, code or name), as document_types lists it. Needed for a new document.")] string? type = null,
        [Description("To change an existing draft: its id. Fields left out keep their values; lines given replace all its lines.")] Guid? id = null,
        [Description("Optional: the category (code, name or id) when the type is in several.")] string? category = null,
        [Description("Posting date (yyyy-mm-dd); today when new and left out.")] DateOnly? postingDate = null,
        [Description("Document date (yyyy-mm-dd); the posting date when new and left out.")] DateOnly? documentDate = null,
        [Description("The from side (e.g. the supplier, or the warehouse goods leave): id or exact code.")] string? from = null,
        [Description("The to side (e.g. the client, or the warehouse goods enter): id or exact code.")] string? to = null,
        [Description("Optional: the external number (the supplier's invoice number, a paper number).")] string? externalNumber = null,
        [Description("Optional: due date (yyyy-mm-dd).")] DateOnly? dueDate = null,
        [Description("Optional: payment method (id, code or name) among those the type takes.")] string? paymentMethod = null,
        [Description("Optional: the opportunity the document is for (id).")] Guid? opportunity = null,
        [Description("Optional: the documents it is made from (ids), e.g. the order an invoice bills.")] IReadOnlyList<Guid>? createdFrom = null,
        [Description("Optional: the type's own properties, by property code: value.")] Dictionary<string, JsonElement>? properties = null,
        [Description("The lines. For a new document at least one, as the type needs.")] IReadOnlyList<DraftLine>? lines = null,
        CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the document as it will be saved (type, sides, lines, amounts)");
            Erp.NeedsPlace(request);
            var existing = id is { } given ? await documents.DocumentAsync(given, ct).ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.") : null;
            if (existing?.Posted == true)
            {
                throw new McpException("The document is posted: a posted document is final and is corrected by a reversal (reverse_document).");
            }

            var allowed = await documents.AllowedTypesAsync(null, ct).ConfigureAwait(false);
            var typeDto = !string.IsNullOrWhiteSpace(type) ? Registers.Type(allowed, type, "document")
                : existing is not null ? allowed.FirstOrDefault(t => t.Id == existing.TypeId) ?? throw new McpException("You may not write documents of this type.")
                : throw new McpException("Give the document type (document_types lists them).");
            var typeId = typeDto.Id!.Value;
            var form = await documents.FormAsync(typeId, Registers.English, ct).ConfigureAwait(false);
            var categoryIds = existing is not null && string.IsNullOrWhiteSpace(category) ? existing.CategoryIds
                : [Registers.Category(await documents.Entities.CategoriesAsync(ct).ConfigureAwait(false), category, "document")
                    ?? (typeDto.CategoryIds.Count == 1 ? typeDto.CategoryIds[0] : throw new McpException("The type is in several categories: give the category."))];

            var fromId = from is null ? existing?.FromId : await SideAsync(form.From, from, ct).ConfigureAwait(false);
            var toId = to is null ? existing?.ToId : await SideAsync(form.To, to, ct).ConfigureAwait(false);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var posting = postingDate ?? existing?.PostingDate ?? today;
            var dated = documentDate ?? existing?.DocumentDate ?? postingDate ?? posting;
            var partnerTable = Table<Partners.Partner>();
            var partner = form.To?.Table == partnerTable ? toId : form.From?.Table == partnerTable ? fromId : null;

            IReadOnlyList<TransactionDto> transactions = existing?.Transactions ?? [];
            if (lines is not null)
            {
                var made = new List<TransactionDto>();
                foreach (var (line, i) in lines.Select((l, i) => (l, i)))
                {
                    made.Add(await LineAsync(form, line, i, partner, dated, ct).ConfigureAwait(false));
                }

                transactions = made;
            }

            var dto = new DocumentDto
            {
                Id = existing?.Id, OwnerId = existing?.OwnerId, IdSup = existing?.IdSup, TypeId = typeId, Code = existing?.Code, Name = existing?.Name ?? [],
                Description = existing?.Description, CategoryIds = categoryIds,
                Values = Registers.Values(typeDto, properties, existing?.Values), AccessUserIds = existing?.AccessUserIds ?? [], AccessRoleIds = existing?.AccessRoleIds ?? [],
                PostingDate = posting, DocumentDate = dated,
                FromId = fromId, FromName = fromId is { } f ? (await Registers.NameOfAsync(db, f, ct).ConfigureAwait(false)).Name ?? existing?.FromName : null,
                ToId = toId, ToName = toId is { } t ? (await Registers.NameOfAsync(db, t, ct).ConfigureAwait(false)).Name ?? existing?.ToName : null,
                ExternalNumber = externalNumber ?? existing?.ExternalNumber, DueDate = dueDate ?? existing?.DueDate,
                PaymentMethodId = paymentMethod is null ? existing?.PaymentMethodId : Option(form.PaymentMethods.Select(o => (o.Id, o.Code, o.Name)), paymentMethod, "payment method"),
                OpportunityId = opportunity ?? existing?.OpportunityId, TableId = existing?.TableId,
                CreatedFromIds = createdFrom ?? existing?.CreatedFromIds ?? [], Transactions = transactions,
            };
            var saved = await documents.SaveAsync(dto, ct).ConfigureAwait(false);
            return await ShowAsync(saved, false, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "make_next_document", Title = "Make the next document", ReadOnly = false, Destructive = false)]
    [Description("Makes a document from another along one of its flows (offer to contract, order to invoice…), as the Generate menu does: an unposted copy of its lines, linked to it, for the user to check and post. First tell the user what will be made from which document and get an explicit yes; then confirm=true. Returns the new draft.")]
    public Task<object> MakeNextDocumentAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The document to make it from.")] Guid id,
        [Description("The document type to make (id, code or name), one next_documents offers.")] string type,
        CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "which document will be made from which");
            Erp.NeedsPlace(request);
            var flows = await documents.FlowsAsync(id, Registers.English, ct).ConfigureAwait(false);
            var key = type.Trim();
            var flow = flows.FirstOrDefault(f => Registers.Is(f.TypeId, key) || string.Equals(f.Code, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException($"This document can't make \"{key}\". It can make: {string.Join(", ", flows.Select(f => f.Code ?? f.Name))}.");
            var made = await documents.MakeAsync(id, flow.TypeId, ct).ConfigureAwait(false);
            return await ShowAsync(made, false, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "post_document", Title = "Post a document", ReadOnly = false, Destructive = true)]
    [Description("Posts a draft document: it becomes final and books what its type books (stock, accounts, open items; offers, contracts and orders book nothing but become final). It can then only be corrected by a reversal. First show the user the document and say that posting is final; get an explicit yes; then confirm=true. Returns the posted document.")]
    public Task<object> PostDocumentAsync([Description("True only after the user said yes.")] bool confirm, [Description("The draft's id.")] Guid id, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the document to post, saying that posting is final");
            Erp.NeedsPlace(request);
            var posted = await documents.PostAsync(id, ct).ConfigureAwait(false);
            return await ShowAsync(posted, false, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "reverse_document", Title = "Reverse a posted document", ReadOnly = false, Destructive = true)]
    [Description("Reverses a posted document, wholly or (when its type allows) only some quantities of its lines: a reversal document is made and posted. get_document shows each line's id and the quantity still reversible. First show the user what will be reversed and say it is final; get an explicit yes; then confirm=true. Returns the reversal.")]
    public Task<object> ReverseDocumentAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The posted document's id.")] Guid id,
        [Description("The reversal's posting date (yyyy-mm-dd); today if left out.")] DateOnly? postingDate = null,
        [Description("Optional, for a partial reversal: line id → quantity to reverse. Left out: the whole document.")] Dictionary<Guid, decimal>? quantities = null,
        CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "what will be reversed, saying that the reversal is posted at once and is final");
            Erp.NeedsPlace(request);
            var reversal = await documents.ReverseAsync(id, quantities is { Count: > 0 } ? quantities : null, null, postingDate ?? DateOnly.FromDateTime(DateTime.Today), ct)
                .ConfigureAwait(false);
            return await ShowAsync(reversal, false, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "save_journal_sheet", Title = "Make or change a draft journal sheet", ReadOnly = false, Destructive = false)]
    [Description("Makes a new draft journal sheet (a general journal, a recurring journal or any type whose rows are journals), or changes a draft sheet (with its id), as the sheet's grid on the screen does: each row is one journal under the sheet, an account with a debit or a credit, its place and description. Debit = credit is checked over the whole sheet when it is posted. Nothing is booked: post it with post_document (the sheet posts with all its rows). Use document_types to find the sheet type; this tool says which row types it takes. First show the user the sheet as it will be saved (rows with accounts, debit, credit, places) and get an explicit yes; then call with confirm=true. Returns the saved sheet with its rows.")]
    public Task<object> SaveJournalSheetAsync(
        [Description("True only after the user said yes to this exact sheet.")] bool confirm,
        [Description("The rows, in order. Changing a sheet: give all its rows; row n changes the sheet's row n, and rows past the given ones are switched off.")] IReadOnlyList<SheetRow> rows,
        [Description("The sheet's document type (id, code or name). Needed for a new sheet.")] string? type = null,
        [Description("To change an existing draft sheet: its id.")] Guid? id = null,
        [Description("Optional: the category (code, name or id) when the type is in several.")] string? category = null,
        [Description("Posting date (yyyy-mm-dd); today when new and left out.")] DateOnly? postingDate = null,
        [Description("Document date (yyyy-mm-dd); the posting date when new and left out.")] DateOnly? documentDate = null,
        [Description("Optional: the sheet's description, e.g. \"Wages October\".")] string? description = null,
        CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the sheet as it will be saved (rows with accounts, debit, credit, places)");
            Erp.NeedsPlace(request);
            var existing = id is { } given ? await documents.DocumentAsync(given, ct).ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.") : null;
            if (existing?.Posted == true)
            {
                throw new McpException("The sheet is posted: a posted document is final and is corrected by a reversal (reverse_document).");
            }

            var allowed = await documents.AllowedTypesAsync(null, ct).ConfigureAwait(false);
            var sheetType = !string.IsNullOrWhiteSpace(type) ? Registers.Type(allowed, type, "document")
                : existing is not null ? allowed.FirstOrDefault(t => t.Id == existing.TypeId) ?? throw new McpException("You may not write documents of this type.")
                : throw new McpException("Give the sheet's document type (document_types lists them).");
            var sheetTypeId = sheetType.Id!.Value;
            var sheetForm = await documents.FormAsync(sheetTypeId, Registers.English, ct).ConfigureAwait(false);
            var rowTypes = allowed.Where(t => t.IdSup == sheetTypeId).ToList();
            if (rowTypes.Count == 0 || sheetForm.LineKinds.Count > 0)
            {
                throw new McpException(sheetForm.LineKinds.Count > 0 ? "This document type is not a sheet: it has lines of its own. Use save_draft_document."
                    : (await documents.Entities.TypesAsync(ct).ConfigureAwait(false)).Any(t => t.IdSup == sheetTypeId)
                        ? "This sheet has row types, but none is given to you: ask an administrator for them."
                        : "This document type is not a sheet: no journal types are under it.");
            }

            if (rows.Count == 0)
            {
                throw new McpException("Give at least one row.");
            }

            var categoryIds = existing is not null && string.IsNullOrWhiteSpace(category) ? existing.CategoryIds
                : [Registers.Category(await documents.Entities.CategoriesAsync(ct).ConfigureAwait(false), category, "document")
                    ?? (sheetType.CategoryIds.Count == 1 ? sheetType.CategoryIds[0] : throw new McpException("The type is in several categories: give the category."))];

            var forms = new Dictionary<Guid, DocumentFormDto>();
            var journals = new List<DocumentDto>();
            var earlier = existing is not null ? await documents.JournalsAsync(existing.Id!.Value, ct).ConfigureAwait(false) : [];
            ElementTypeDto? lastType = null;
            string? lastPlace = null;
            foreach (var (row, i) in rows.Select((r, i) => (r, i)))
            {
                var rowType = !string.IsNullOrWhiteSpace(row.Type) ? Registers.Type(rowTypes, row.Type, "row")
                    : lastType ?? (rowTypes.Count == 1 ? rowTypes[0]
                        : throw new McpException($"Row {i + 1}: choose the row's type: {string.Join(", ", rowTypes.Select(t => t.Code ?? Registers.Name(t.Name)))}."));
                lastType = rowType;
                var rowTypeId = rowType.Id!.Value;
                if (!forms.TryGetValue(rowTypeId, out var form))
                {
                    form = await documents.FormAsync(rowTypeId, Registers.English, ct).ConfigureAwait(false);
                    forms[rowTypeId] = form;
                }

                var (debit, credit) = (row.Debit ?? 0, row.Credit ?? 0);
                if (debit < 0 || credit < 0 || (debit == 0) == (credit == 0))
                {
                    throw new McpException($"Row {i + 1}: give a debit or a credit (one of them, not negative).");
                }

                var (kindId, confId) = KindOf(form, row.Kind, i);
                var element = await RowElementAsync(row.Account, i, ct).ConfigureAwait(false);
                var place = string.IsNullOrWhiteSpace(row.Place) ? lastPlace : row.Place;
                lastPlace = place;
                var locationSide = form.From?.Table == Table<CostCenters.CostCenter>() ? form.From : form.To?.Table == Table<CostCenters.CostCenter>() ? form.To : null;
                Guid? placeId = locationSide is not null && !string.IsNullOrWhiteSpace(place) ? await SideAsync(locationSide, place, ct).ConfigureAwait(false) : null;
                var placeName = placeId is { } p ? (await Registers.NameOfAsync(db, p, ct).ConfigureAwait(false)).Name : null;
                var onFrom = form.From?.Table == Table<CostCenters.CostCenter>();
                var onTo = form.To?.Table == Table<CostCenters.CostCenter>();
                // Row n keeps the sheet's row n, as the grid keeps its rows; rows past the given ones are switched off by the save.
                var kept = i < earlier.Count ? earlier[i] : null;
                journals.Add(new DocumentDto
                {
                    Id = kept?.Id, OwnerId = kept?.OwnerId, Code = kept?.Code, Values = kept?.Values ?? [], AccessUserIds = kept?.AccessUserIds ?? [], AccessRoleIds = kept?.AccessRoleIds ?? [],
                    TypeId = rowTypeId, CategoryIds = [], Name = kept?.Name ?? [],
                    Description = string.IsNullOrWhiteSpace(row.Description) ? null : new Dictionary<string, string> { [Registers.English] = row.Description.Trim() },
                    Transactions =
                    [
                        new TransactionDto
                        {
                            TypeId = kindId, TypeConfId = confId, ElementId = element, ElementName = (await Registers.NameOfAsync(db, element, ct).ConfigureAwait(false)).Name,
                            Quantity = 1, Price = debit + credit, Debit = debit, Credit = credit,
                            VatGroupId = form.VatGroups.FirstOrDefault(v => v.Rate == 0)?.Id,
                            FromId = onFrom ? placeId : null, FromName = onFrom ? placeName : null, ToId = onTo ? placeId : null, ToName = onTo ? placeName : null,
                        },
                    ],
                });
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var posting = postingDate ?? existing?.PostingDate ?? today;
            var sheet = new DocumentDto
            {
                Id = existing?.Id, OwnerId = existing?.OwnerId, TypeId = sheetTypeId, Code = existing?.Code, Name = existing?.Name ?? [],
                Description = description is null ? existing?.Description : new Dictionary<string, string> { [Registers.English] = description.Trim() },
                CategoryIds = categoryIds, Values = existing?.Values ?? Registers.Values(sheetType, null, null),
                AccessUserIds = existing?.AccessUserIds ?? [], AccessRoleIds = existing?.AccessRoleIds ?? [],
                PostingDate = posting, DocumentDate = documentDate ?? existing?.DocumentDate ?? postingDate ?? posting, Transactions = [],
            };
            var saved = await documents.SaveSheetAsync(new SheetDto(sheet, journals), ct).ConfigureAwait(false);
            return await ShowAsync(saved.Sheet, false, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "repeat_document", Title = "Repeat a document on another day", ReadOnly = false, Destructive = true)]
    [Description("Does a document again for another day, as the Repeat button on the document screen does (e.g. a recurring journal sheet each month: wages, rent): a copy of the document, and of a sheet's rows, dated that day and linked to the original. With post=true the copy is posted at once, which is final; with post=false it stays a draft for the user to check and change. Nothing repeats by itself: this is done each time a person asks. A reversal is not repeated. First tell the user which document will be repeated on which date, and whether it will be posted (final); get an explicit yes; then confirm=true. Returns the copy.")]
    public Task<object> RepeatDocumentAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The document (or sheet) to repeat: its id.")] Guid id,
        [Description("The day of the copy (yyyy-mm-dd): its posting and document date.")] DateOnly date,
        [Description("True to post the copy at once (final); false (default) to keep it a draft.")] bool post = false,
        CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, post ? "which document will be repeated on which date, saying that the copy is posted at once and is final"
                : "which document will be repeated on which date, as a draft");
            Erp.NeedsPlace(request);
            var copy = await documents.RepeatAsync(id, date, post, ct).ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.");
            return await ShowAsync(copy, false, ct).ConfigureAwait(false);
        });

    // ---------- Helpers ----------

    /// <summary>A row's kind of line and what it holds, as the sheet's grid offers them ("kind · holds" when a kind holds several things).</summary>
    private static (Guid Kind, Guid? Conf) KindOf(DocumentFormDto form, string? given, int i)
    {
        var options = form.LineKinds.SelectMany(k => k.Scopes.Select(s => (Kind: k, Scope: s))).ToList();
        string Label((LineKindDto Kind, LineScopeDto Scope) o) => o.Kind.Scopes.Count > 1 ? $"{o.Kind.Code ?? o.Kind.Name} · {o.Scope.Name}" : o.Kind.Code ?? o.Kind.Name;
        if (string.IsNullOrWhiteSpace(given))
        {
            return options.Count == 1 ? (options[0].Kind.TransactionTypeId, options[0].Scope.ConfId)
                : form.LineKinds.Count == 1 && form.LineKinds[0].Scopes.Count == 0 ? (form.LineKinds[0].TransactionTypeId, null)
                : throw new McpException($"Row {i + 1}: choose the kind of line: {string.Join(", ", options.Select(Label))}.");
        }

        var key = given.Trim();
        var parts = key.Split('·', 2, StringSplitOptions.TrimEntries);
        bool KindIs(LineKindDto k, string s) => Registers.Is(k.TransactionTypeId, s) || string.Equals(k.Code, s, StringComparison.OrdinalIgnoreCase)
            || string.Equals(k.Name, s, StringComparison.OrdinalIgnoreCase);
        var found = options.Where(o => parts.Length == 2 ? KindIs(o.Kind, parts[0]) && string.Equals(o.Scope.Name, parts[1], StringComparison.OrdinalIgnoreCase) : KindIs(o.Kind, key)).ToList();
        if (found.Count == 0 && parts.Length == 1 && form.LineKinds.FirstOrDefault(k => KindIs(k, key)) is { Scopes.Count: 0 } bare)
        {
            return (bare.TransactionTypeId, null);
        }

        return found.Count == 1 ? (found[0].Kind.TransactionTypeId, found[0].Scope.ConfId)
            : throw new McpException(found.Count == 0 ? $"Row {i + 1}: the row's type has no kind of line \"{key}\". It has: {string.Join(", ", options.Select(Label))}."
                : $"Row {i + 1}: \"{key}\" holds several things; choose one: {string.Join(", ", found.Select(Label))}.");
    }

    /// <summary>A row's account (or partner, or product): an id, or an exact code among those the user may see.</summary>
    private async Task<Guid> RowElementAsync(string given, int i, CancellationToken ct)
    {
        var key = given.Trim();
        if (Guid.TryParse(key, out var id))
        {
            return id;
        }

        var accounts = await Registers.Charts(db, request, visibility).LookupAsync(null, null, key, Registers.English, 1, Registers.MaxPage, ct).ConfigureAwait(false);
        var partners = await Registers.Partners(db, request, visibility).LookupAsync(null, null, key, Registers.English, 1, Registers.MaxPage, ct).ConfigureAwait(false);
        var products = await Registers.Products(db, request, visibility).LookupAsync(null, null, key, Registers.English, 1, Registers.MaxPage, ct).ConfigureAwait(false);
        var exact = accounts.Items.Concat(partners.Items).Concat(products.Items).Where(o => string.Equals(o.Code, key, StringComparison.OrdinalIgnoreCase)).Select(o => o.Id).Distinct().ToList();
        return exact.Count == 1 ? exact[0]
            : throw new McpException(exact.Count == 0 ? $"Row {i + 1}: no account, partner or product with the code \"{key}\" that you may see."
                : $"Row {i + 1}: several accounts, partners or products have the code \"{key}\": give the id.");
    }

    private string? Table<T>() => db.Model.FindEntityType(typeof(T))?.GetTableName();

    /// <summary>A side of a document: an id, or a code in the register the side picks from (with the user's rights there).</summary>
    private async Task<Guid> SideAsync(DocumentSideDto? side, string given, CancellationToken ct)
    {
        if (Guid.TryParse(given.Trim(), out var id))
        {
            return id;
        }

        if (side is null)
        {
            throw new McpException("This document type has no such side.");
        }

        Func<string, Task<PageDto<LookupOptionDto>>> lookup = side.Table == Table<Partners.Partner>() ? s => Registers.Partners(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct)
            : side.Table == Table<CostCenters.CostCenter>() ? s => Registers.CostCenters(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct)
            : side.Table == Table<Charts.Chart>() ? s => Registers.Charts(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct)
            : side.Table == Table<Employees.Employee>() ? s => Employees(s, ct)
            : throw new McpException($"Give the {side.Name} side by its id.");
        return await Registers.ElementAsync(lookup, given, side.Name).ConfigureAwait(false);
    }

    private Task<PageDto<LookupOptionDto>> Employees(string search, CancellationToken ct) =>
        new EntityService<Employees.EmployeeCategory, Employees.EmployeeType, Employees.InterEmployeeTypeCategory, Employees.EmployeeTypeInfo, Employees.EmployeeTypeInfoChoice,
            Employees.Employee, Employees.EmployeeInfo, Employees.InterEmployeeCategory, Employees.EmployeeAccess>(db, request, visibility)
            .LookupAsync(null, null, search, Registers.English, 1, Registers.MaxPage, ct);

    private static Guid Option(IEnumerable<(Guid Id, string? Code, string Name)> options, string given, string what)
    {
        var key = given.Trim();
        var list = options.ToList();
        var found = list.Where(o => Registers.Is(o.Id, key) || string.Equals(o.Code, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(o.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count == 1 ? found[0].Id
            : throw new McpException($"The {what} \"{key}\" is not one this document takes. It takes: {string.Join(", ", list.Select(o => o.Code ?? o.Name))}.");
    }

    /// <summary>A line as the editor makes it: its kind and scope, the element's name, and the unit, price and VAT group proposed for a product when not given.</summary>
    private async Task<TransactionDto> LineAsync(DocumentFormDto form, DraftLine line, int i, Guid? partner, DateOnly day, CancellationToken ct)
    {
        Guid? element = null;
        if (!string.IsNullOrWhiteSpace(line.Element))
        {
            var key = line.Element.Trim();
            if (Guid.TryParse(key, out var id))
            {
                element = id;
            }
            else
            {
                var products = await Registers.Products(db, request, visibility).LookupAsync(null, null, key, Registers.English, 1, Registers.MaxPage, ct).ConfigureAwait(false);
                var accounts = await Registers.Charts(db, request, visibility).LookupAsync(null, null, key, Registers.English, 1, Registers.MaxPage, ct).ConfigureAwait(false);
                var exact = products.Items.Concat(accounts.Items).Where(o => string.Equals(o.Code, key, StringComparison.OrdinalIgnoreCase)).Select(o => o.Id).Distinct().ToList();
                element = exact.Count == 1 ? exact[0]
                    : throw new McpException(exact.Count == 0 ? $"Line {i + 1}: no product or account with the code \"{key}\" that you may see."
                        : $"Line {i + 1}: several products or accounts have the code \"{key}\": give the id.");
            }
        }

        var isProduct = element is { } e && await db.Set<Products.Product>().AsNoTracking().AnyAsync(p => p.Id == e, ct).ConfigureAwait(false);
        var currency = string.IsNullOrWhiteSpace(line.Currency) ? (Guid?)null : Option(form.Currencies.Select(c => (c.Id, c.Code, c.Name)), line.Currency, "currency");
        LineProposalDto? proposal = null;
        if (isProduct)
        {
            Guid? askedUnit = null;
            if (!string.IsNullOrWhiteSpace(line.Unit) && Guid.TryParse(line.Unit.Trim(), out var u))
            {
                askedUnit = u;
            }

            proposal = await documents.LineProposalAsync(form.TypeId, element!.Value, partner, askedUnit, currency, Registers.English, ct, day).ConfigureAwait(false);
        }

        // The kind of line: the one given, the only one, or the one whose scope holds the product.
        LineKindDto kind;
        if (!string.IsNullOrWhiteSpace(line.Kind))
        {
            var key = line.Kind.Trim();
            kind = form.LineKinds.FirstOrDefault(k => Registers.Is(k.TransactionTypeId, key) || string.Equals(k.Code, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(k.Name, key, StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException($"Line {i + 1}: the document type has no kind of line \"{key}\". It has: {string.Join(", ", form.LineKinds.Select(k => k.Code ?? k.Name))}.");
        }
        else
        {
            var holding = proposal is null ? [] : form.LineKinds.Where(k => k.Scopes.Any(s => s.ScopeId is { } scope && proposal.ScopeIds.Contains(scope))).ToList();
            kind = form.LineKinds.Count == 1 ? form.LineKinds[0] : holding.Count == 1 ? holding[0]
                : throw new McpException($"Line {i + 1}: choose the kind of line: {string.Join(", ", form.LineKinds.Select(k => k.Code ?? k.Name))}.");
        }

        var scopes = kind.Scopes.Where(s => proposal is null || s.ScopeId is not { } scope || proposal.ScopeIds.Contains(scope)).ToList();
        Guid? conf = kind.Scopes.Count == 1 ? kind.Scopes[0].ConfId : scopes.Count == 1 ? scopes[0].ConfId : null;

        Guid? unit = null;
        if (!string.IsNullOrWhiteSpace(line.Unit))
        {
            var key = line.Unit.Trim();
            unit = Guid.TryParse(key, out var u) ? u
                : proposal?.Units.FirstOrDefault(o => string.Equals(o.Code, key, StringComparison.OrdinalIgnoreCase) || string.Equals(o.Name, key, StringComparison.OrdinalIgnoreCase))?.Id
                ?? throw new McpException($"Line {i + 1}: the unit \"{key}\" is not one of the product's: {string.Join(", ", proposal?.Units.Select(o => o.Code ?? o.Name) ?? [])}.");
        }

        var vat = string.IsNullOrWhiteSpace(line.VatGroup) ? proposal?.VatGroupId : Option(form.VatGroups.Select(v => (v.Id, v.Code, v.Name)), line.VatGroup, "VAT group");
        var price = line.Price ?? proposal?.Price;
        if (line.Price is null && proposal?.Price is not null && currency is null)
        {
            currency = proposal.CurrencyId;
        }

        return new TransactionDto
        {
            TypeId = kind.TransactionTypeId, TypeConfId = conf, ElementId = element,
            ElementName = element is { } el ? (await Registers.NameOfAsync(db, el, ct).ConfigureAwait(false)).Name : null,
            UnitId = unit ?? proposal?.UnitId, VatGroupId = vat, Price = price, Discount = line.Discount, Quantity = line.Quantity, CurrencyId = currency,
            ExchangeRate = line.ExchangeRate, FromId = line.From, ToId = line.To,
            FromName = line.From is { } from ? (await Registers.NameOfAsync(db, from, ct).ConfigureAwait(false)).Name : null,
            ToName = line.To is { } to ? (await Registers.NameOfAsync(db, to, ct).ConfigureAwait(false)).Name : null,
            Lot = line.Lot, Serials = [.. (line.Serials ?? []).Select(s => new TransactionSerialDto(s))],
        };
    }

    /// <summary>A type's form, compact: sides, kinds of lines, payment methods, VAT groups, currencies, properties.</summary>
    private async Task<object> FormOfAsync(ElementTypeDto type, CancellationToken ct)
    {
        var form = await documents.FormAsync(type.Id!.Value, Registers.English, ct).ConfigureAwait(false);
        return new
        {
            typeId = form.TypeId, form.Code, form.Name, categoryIds = type.CategoryIds,
            from = form.From?.Name, to = form.To?.Name, bookedWhenPosted = !form.NeverPosted, form.IsReversal, form.AllowsPartialReversal, form.HoldsOpenItem, form.PaysOpenItems,
            lineKinds = form.LineKinds.Select(k => new
            {
                k.TransactionTypeId, k.Code, k.Name, holds = k.Scopes.Select(s => s.Name),
                needs = k.Fill is null ? null : Needs(k.Fill), defaultQuantity = k.Fill?.DefaultQuantity,
            }),
            paymentMethods = form.PaymentMethods.Select(o => new { o.Id, o.Code, o.Name }),
            vatGroups = form.VatGroups.Select(v => new { v.Id, v.Code, v.Name, v.Rate }),
            currencies = form.Currencies.Select(c => new { c.Id, c.Code, c.IsBase }),
            properties = Registers.Properties(type),
        };
    }

    // The fields a line must have (0 typed or chosen, 1 chosen from the database); 2 = not needed is left out.
    private static IEnumerable<string> Needs(LineFillDto fill) =>
        new (string Field, int? Mode)[]
        {
            ("product or account", fill.Element), ("unit", fill.Unit), ("place", fill.Location), ("price", fill.UnitPrice), ("quantity", fill.Quantity), ("VAT group", fill.VatGroup),
            ("discount", fill.Discount),
        }.Where(f => f.Mode is 0 or 1).Select(f => f.Field);

    /// <summary>A document as Claude reads it: the header, the lines, the totals, its links.</summary>
    private async Task<object> ShowAsync(DocumentDto d, bool full, CancellationToken ct)
    {
        var types = await documents.Entities.TypesAsync(ct).ConfigureAwait(false);
        var typeName = Registers.Name(types.FirstOrDefault(t => t.Id == d.TypeId)?.Name);
        var lineTypes = d.Transactions.Select(t => t.TypeId).Distinct().ToList();
        var kinds = (await db.Set<Transactions.TransactionType>().AsNoTracking().Where(t => lineTypes.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(t => t.Id, t => t.Name.In(Registers.English));
        var vatIds = d.Transactions.Select(t => t.VatGroupId).OfType<Guid>().Distinct().ToList();
        var vats = (await db.Set<Gamma.Erp.Models.Payments.VatGroup>().AsNoTracking().Where(v => vatIds.Contains(v.Id)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(v => v.Id, v => v.Code);
        var currencyIds = d.Totals.Select(t => t.CurrencyId).Concat(d.Transactions.Select(t => t.CurrencyId)).OfType<Guid>().Distinct().ToList();
        var currencies = (await db.Set<Gamma.Erp.Models.Currencies.Currency>().AsNoTracking().Where(c => currencyIds.Contains(c.Id)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(c => c.Id, c => c.Code);
        var sources = d.CreatedFromIds.ToList();
        var from = await db.Set<Gamma.Erp.Models.Documents.Document>().AsNoTracking().Where(x => sources.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<Guid, decimal>? reversible = null;
        IReadOnlyList<MadeDocumentDto> made = [];
        if (full)
        {
            made = await documents.MadeFromAsync(d.Id!.Value, Registers.English, ct).ConfigureAwait(false);
            if (d.Posted)
            {
                try
                {
                    reversible = (await documents.ReversibleAsync(d.Id!.Value, ct).ConfigureAwait(false)).ToDictionary(r => r.Id, r => r.Left);
                }
                catch (ValidationException)
                {
                    // Not reversible (a reversal itself, a type without one): no quantities to show.
                }
            }
        }

        // A sheet has no lines of its own: its rows are the journals under it.
        var journals = d.Transactions.Count == 0 ? await documents.JournalsAsync(d.Id!.Value, ct).ConfigureAwait(false) : [];
        var rows = journals.Count == 0 ? null : journals.Select((j, i) => new
        {
            j.Id, n = i + 1, number = j.Code, type = Registers.Name(types.FirstOrDefault(t => t.Id == j.TypeId)?.Name),
            account = j.Transactions.Count > 0 ? j.Transactions[0].ElementName : null, accountId = j.Transactions.Count > 0 ? j.Transactions[0].ElementId : null,
            place = j.Transactions.Count > 0 ? j.Transactions[0].FromName ?? j.Transactions[0].ToName : null,
            debit = j.Transactions.Sum(t => t.Debit ?? 0), credit = j.Transactions.Sum(t => t.Credit ?? 0),
            description = j.Description is { } text ? text.GetValueOrDefault(Registers.English) ?? text.Values.FirstOrDefault() : null,
        }).ToList();

        return new
        {
            d.Id, number = d.Code, type = typeName, typeId = d.TypeId, state = d.Posted ? "posted" : "draft", d.PostingDate, d.DocumentDate, d.DueDate, d.ExternalNumber,
            from = d.FromName, fromId = d.FromId, to = d.ToName, toId = d.ToId, d.PaymentMethodId, d.OpportunityId,
            reverses = d.ReversedDocumentCode,
            lines = d.Transactions.Select((t, i) => new
            {
                t.Id, n = i + 1, kind = kinds.GetValueOrDefault(t.TypeId), element = t.ElementName, elementId = t.ElementId, unit = t.UnitName, t.Quantity, t.Price, t.Discount,
                vatGroup = t.VatGroupId is { } v ? vats.GetValueOrDefault(v) : null, t.Value, t.Vat, total = t.ValueVat,
                currency = t.CurrencyId is { } c ? currencies.GetValueOrDefault(c) : null, t.ExchangeRate, totalLcu = t.ValueVatLcu,
                from = t.FromName, to = t.ToName, t.Debit, t.Credit, t.Lot, serials = t.Serials.Count == 0 ? null : t.Serials.Select(s => s.Serial),
                reversibleLeft = reversible is null ? (decimal?)null : reversible.GetValueOrDefault(t.Id!.Value),
            }),
            totals = d.Totals.Select(t => new
            {
                currency = t.CurrencyId is { } c ? currencies.GetValueOrDefault(c) : null, t.Value, t.Vat, total = t.ValueVat, t.ExchangeRate, valueLcu = t.ValueLcu, vatLcu = t.VatLcu,
                totalLcu = t.ValueVatLcu,
            }),
            sheetRows = rows, sheetDebit = rows?.Sum(r => r.debit), sheetCredit = rows?.Sum(r => r.credit),
            madeFrom = from.Select(x => new { x.Id, number = x.Code }),
            madeFromIt = full ? made.Select(m => new { m.Id, number = m.Code, m.Type, state = m.Posted ? "posted" : "draft" }) : null,
        };
    }
}

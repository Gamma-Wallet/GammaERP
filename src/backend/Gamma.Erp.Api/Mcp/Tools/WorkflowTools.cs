using System.ComponentModel;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Currencies;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Mail;
using Gamma.Erp.Services.Reports;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>
/// What a person does around documents besides writing and posting them, each as its screen does it: switching a draft off, holding one while
/// editing it, e-mailing one, giving a document type to users, approving, the contract invoice run and renewals, closing a year, and the
/// company's currencies. Nothing runs by itself: each is started by the user's yes.
/// </summary>
[McpServerToolType]
public sealed class WorkflowTools(ErpDbContext db, RequestContext request, Visibility visibility, DocumentService documents, MailService mail, Approvals approvals,
    ContractBillingService billing, YearClosing closing, CurrencyService currencies)
{
    [McpServerTool(Name = "switch_off_document", Title = "Switch off a draft or an unbooked document", ReadOnly = false, Destructive = true)]
    [Description("Switches a document off, as the editor's delete does: a draft, or a posted offer, contract or order (they booked nothing, so this cancels it), with the documents under it. Nothing is deleted. Refused for a posted document that booked something (it is corrected by a reversal), when documents were made from it, or while another user edits it. Show the user the document and get an explicit yes; then confirm=true.")]
    public Task<object> SwitchOffDocumentAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The document's id.")] Guid id, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the document to switch off");
            Erp.NeedsPlace(request);
            var document = await documents.DocumentAsync(id, ct).ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.");
            await documents.DeleteAsync(id, ct).ConfigureAwait(false);
            return new { switchedOff = true, document.Id, document.Code, document.Posted };
        });

    [McpServerTool(Name = "hold_document", Title = "Hold or let go of a document", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("A document's hold, as the editor takes it: action holder (who holds it now), hold (no one else may change it while you work on it), or release (let it go). Holding changes nothing in the document.")]
    public Task<object> HoldDocumentAsync(
        [Description("holder, hold or release.")] string action,
        [Description("The document's id.")] Guid id, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            switch (action.Trim().ToUpperInvariant())
            {
                case "HOLD":
                    await documents.HoldAsync(id, ct).ConfigureAwait(false);
                    break;
                case "RELEASE":
                    await documents.ReleaseAsync(id, ct).ConfigureAwait(false);
                    break;
                case "HOLDER":
                    break;
                default:
                    throw new McpException("The action is holder, hold or release.");
            }

            var holder = await documents.HolderAsync(id, ct).ConfigureAwait(false);
            return new { id, holder, heldByMe = holder is not null && holder == request.UserId };
        });

    [McpServerTool(Name = "email_document", Title = "E-mail a document", ReadOnly = false, Destructive = false)]
    [Description("Sends a document by e-mail as its PDF, as the editor's E-mail does, through the company's mail server (refused when e-mail is not set up). Show the user the addresses, subject and message and get an explicit yes; then confirm=true. Returns the message id.")]
    public Task<object> EmailDocumentAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The document's id.")] Guid id,
        [Description("The addresses to send it to.")] IReadOnlyList<string> to,
        [Description("Optional: the subject.")] string? subject = null,
        [Description("Optional: the message.")] string? message = null,
        [Description("Optional: the PDF's language (e.g. it); English when left out.")] string? language = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the addresses, subject and message of the e-mail");
            Erp.NeedsPlace(request);
            var sent = await mail.SendDocumentAsync(id, new SendDocumentRequest(to, subject, message, string.IsNullOrWhiteSpace(language) ? Registers.English : language.Trim()), ct)
                .ConfigureAwait(false) ?? throw new McpException("There is no such document that you may see.");
            return new { sent = true, messageId = sent };
        });

    [McpServerTool(Name = "set_document_type_users", Title = "Who may write a document type", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Gives a document type to users and roles, as the type's Users tab does: the whole lists (others are taken away). Administrators only. Show the user the lists and get an explicit yes; then confirm=true.")]
    public Task<object> SetDocumentTypeUsersAsync(
        [Description("True only after the administrator said yes.")] bool confirm,
        [Description("The document type: id, code or name.")] string type,
        [Description("The users (ids or exact codes or names), the whole list.")] IReadOnlyList<string>? users = null,
        [Description("The roles (codes, names or ids), the whole list.")] IReadOnlyList<string>? roles = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the users and roles the document type is given to");
            Erp.NeedsPlace(request);
            Registers.Administrator(request);
            var typeDto = Registers.Type(await documents.Entities.TypesAsync(ct).ConfigureAwait(false), type, "document");
            var userRows = Registers.Users(db, request, visibility);
            var userIds = new List<Guid>();
            foreach (var user in users ?? [])
            {
                userIds.Add(await Registers.ElementAsync(s => userRows.LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), user, "user").ConfigureAwait(false));
            }

            var roleList = await userRows.CategoriesAsync(ct).ConfigureAwait(false);
            var roleIds = (roles ?? []).Select(r => Registers.Category(roleList, r, "role")!.Value).ToList();
            await documents.SaveTypeUsersAsync(typeDto.Id!.Value, userIds, roleIds, ct).ConfigureAwait(false);
            return new { type = typeDto.Code ?? Registers.Name(typeDto.Name), users = userIds, roles = roleIds };
        });

    [McpServerTool(Name = "approvals", Title = "Documents waiting for approval", ReadOnly = true)]
    [Description("The documents waiting for an approval (over an approval limit), each with type, partner, total, who wrote it, date and whether this user may approve it. Needs the Approvals screen.")]
    public Task<object> ApprovalsAsync(CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "approvals", ct).ConfigureAwait(false);
            return await approvals.WaitingAsync(Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "approve_document", Title = "Approve a document", ReadOnly = false, Destructive = false)]
    [Description("Approves a document waiting for approval, as the Approvals screen does (by hand, within the user's approval limit); after that it can be posted. Needs the Approvals screen. Show the user the document and get an explicit yes; then confirm=true.")]
    public Task<object> ApproveDocumentAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The document's id (approvals lists them).")] Guid id, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the document to approve");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "approvals", ct).ConfigureAwait(false);
            await approvals.ApproveAsync(id, ct).ConfigureAwait(false);
            return new { approved = true, id };
        });

    [McpServerTool(Name = "contract_billing", Title = "Contracts to bill or renew", ReadOnly = true)]
    [Description("Contract billing as its screens show it: what=due (contracts whose next invoice falls by a day; needs Invoice run), what=renewals (contracts ending within some days; needs Renewals), or what=plan (one contract's plan: every how many months, next date; needs Documents).")]
    public Task<object> ContractBillingAsync(
        [Description("due, renewals or plan.")] string what,
        [Description("due: the last day to bill up to; today when left out.")] DateOnly? until = null,
        [Description("renewals: within how many days they end; 30 when left out.")] int? days = null,
        [Description("plan: the contract's id.")] Guid? contract = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            switch (what.Trim().ToUpperInvariant())
            {
                case "DUE":
                    await Registers.Screen(visibility, "invoice-run", ct).ConfigureAwait(false);
                    return await billing.DueAsync(until ?? DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false);
                case "RENEWALS":
                    await Registers.Screen(visibility, "renewals", ct).ConfigureAwait(false);
                    return await billing.RenewalsAsync(days ?? 30, ct).ConfigureAwait(false);
                case "PLAN":
                    await Registers.Screen(visibility, "documents", ct).ConfigureAwait(false);
                    return (object?)await billing.PlanAsync(contract ?? throw new McpException("Give the contract."), ct).ConfigureAwait(false) ?? new { plan = (object?)null };
                default:
                    throw new McpException("What is due, renewals or plan.");
            }
        });

    [McpServerTool(Name = "run_contract_billing", Title = "Invoice run, renewal or a contract's plan", ReadOnly = false, Destructive = true)]
    [Description("Starts contract billing by hand, as its screens do: action run (invoices every contract due by a day; the invoices are made and posted, so final; needs Invoice run), renew (a contract to a new end date, as a new contract; needs Renewals), or plan (sets a contract's billing: every how many months and the next date; needs Documents). Show the user contract_billing's list or the change and get an explicit yes; then confirm=true.")]
    public Task<object> RunContractBillingAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("run, renew or plan.")] string action,
        [Description("run: the last day to bill up to.")] DateOnly? until = null,
        [Description("renew, plan: the contract's id.")] Guid? contract = null,
        [Description("renew: the new end date.")] DateOnly? endDate = null,
        [Description("plan: every how many months it is billed (empty stops it).")] int? everyMonths = null,
        [Description("plan: the next invoice date.")] DateOnly? nextDate = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the contracts to bill, the renewal or the new plan");
            Erp.NeedsPlace(request);
            switch (action.Trim().ToUpperInvariant())
            {
                case "RUN":
                    await Registers.Screen(visibility, "invoice-run", ct).ConfigureAwait(false);
                    return await billing.RunAsync(until ?? throw new McpException("Give the last day to bill up to."), null, ct).ConfigureAwait(false);
                case "RENEW":
                    await Registers.Screen(visibility, "renewals", ct).ConfigureAwait(false);
                    var renewed = await billing.RenewAsync(contract ?? throw new McpException("Give the contract."), endDate ?? throw new McpException("Give the new end date."), ct)
                        .ConfigureAwait(false);
                    return new { renewed.Id, renewed.Code, renewed.Posted };
                case "PLAN":
                    await Registers.Screen(visibility, "documents", ct).ConfigureAwait(false);
                    var id = contract ?? throw new McpException("Give the contract.");
                    await billing.SetPlanAsync(id, everyMonths, nextDate, ct).ConfigureAwait(false);
                    return (object?)await billing.PlanAsync(id, ct).ConfigureAwait(false) ?? new { plan = (object?)null };
                default:
                    throw new McpException("The action is run, renew or plan.");
            }
        });

    [McpServerTool(Name = "year_closing", Title = "Close a fiscal year", ReadOnly = false, Destructive = true)]
    [Description("A fiscal year's closing, as its screen does: action check (the year a day falls in: its period, drafts left, debit and credit, whether closed and whether it can be closed), or close (locks the year: final; nothing more is posted in it). Needs the Year closing screen. For close, show the user the check, say it is final, and get an explicit yes; then confirm=true.")]
    public Task<object> YearClosingAsync(
        [Description("check or close.")] string action,
        [Description("A day of the fiscal year.")] DateOnly day,
        [Description("close: optional, the next year's currency (id or code) when it changes.")] string? nextCurrency = null,
        [Description("True only after the user said yes (close).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "year-closing", ct).ConfigureAwait(false);
            switch (action.Trim().ToUpperInvariant())
            {
                case "CHECK":
                    return await closing.CheckAsync(day, ct).ConfigureAwait(false);
                case "CLOSE":
                    Erp.NeedsYes(confirm, "the year to close (final)");
                    Guid? next = nextCurrency is null ? null : await CurrencyAsync(nextCurrency, ct).ConfigureAwait(false);
                    return await closing.CloseAsync(day, ct, next).ConfigureAwait(false);
                default:
                    throw new McpException("The action is check or close.");
            }
        });

    [McpServerTool(Name = "save_currency", Title = "Add, change or switch off a currency", ReadOnly = false, Destructive = false)]
    [Description("The company's currencies, as the Currencies screen does: adds one (ISO code and name), changes one, or switches one off. Administrators only, with the Currencies screen. Show the user the change and get an explicit yes; then confirm=true.")]
    public Task<object> SaveCurrencyAsync(
        [Description("True only after the administrator said yes.")] bool confirm,
        [Description("To change or switch off: the currency (id or code). Left out: a new one.")] string? currency = null,
        [Description("Its ISO code (e.g. CHF).")] string? code = null,
        [Description("Its name.")] string? name = null,
        [Description("True switches it off.")] bool switchOff = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the currency to save or switch off");
            Erp.NeedsPlace(request);
            Registers.Administrator(request);
            await Registers.Screen(visibility, "currencies", ct).ConfigureAwait(false);
            Guid? id = currency is null ? null : await CurrencyAsync(currency, ct).ConfigureAwait(false);
            if (switchOff)
            {
                await currencies.SwitchOffCurrencyAsync(id ?? throw new McpException("Give the currency to switch off."), ct).ConfigureAwait(false);
                return new { switchedOff = true, id };
            }

            return await currencies.SaveCurrencyAsync(new SaveCurrencyRequest(id, code?.Trim(), name?.Trim()), ct).ConfigureAwait(false);
        });

    private async Task<Guid> CurrencyAsync(string given, CancellationToken ct)
    {
        var key = given.Trim();
        var list = await currencies.CurrenciesAsync(Registers.English, ct).ConfigureAwait(false);
        var found = list.Where(c => Registers.Is(c.Id, key) || string.Equals(c.Code, key, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count == 1 ? found[0].Id : throw new McpException($"No currency \"{key}\". Known: {string.Join(", ", list.Select(c => c.Code))}.");
    }
}

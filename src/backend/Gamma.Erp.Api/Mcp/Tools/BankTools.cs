using System.ComponentModel;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Banking;
using Gamma.Erp.Services.Entities;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>
/// The company's bank accounts and their connections to the bank through Claude (the founder, 2026-11-14): the same services as the Bank statements screen.
/// Signing in at the bank is always done by the person in their own browser; Claude only hands them the link. Reads of the bank started through Claude
/// carry no browser of the person, so the bank may count them as background reads (about four a day).
/// </summary>
[McpServerToolType]
public sealed class BankTools(RequestContext request, Visibility visibility, BankConnections connections, BankFeeds feeds)
{
    [McpServerTool(Name = "bank_accounts", Title = "Bank accounts and their connections", ReadOnly = true)]
    [Description("Lists the company's bank accounts (cost centres that take bank statements) with their IBAN, BIC, holder, currency, and connection to the bank: connected (at which bank, until when, last day fetched), waiting for the sign-in, or several accounts to choose from. With country: the banks the company's open banking provider reaches there. Needs the Bank statements screen.")]
    public Task<object> BankAccountsAsync(
        [Description("Optional: a two-letter country code, for the banks the provider reaches there (to connect an account).")] string? country = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "bank-statements", ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(country) ? await connections.ListAsync(Registers.English, ct).ConfigureAwait(false)
                : await connections.AspspsAsync(country, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "set_bank_account_iban", Title = "Set a bank account's IBAN", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Sets a bank account's IBAN, BIC and holder by hand (enough for a payment QR code without a connection). The IBAN is checked and one account's only. A connected account keeps the IBAN its bank gave. Needs the Bank statements screen. Show the user the values and get an explicit yes; then confirm=true.")]
    public Task<object> SetBankAccountIbanAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The bank account cost centre's id (bank_accounts lists them).")] Guid bankId,
        [Description("The IBAN; spaces are allowed.")] string? iban,
        [Description("Optional: the BIC, 8 or 11 letters and digits.")] string? bic = null,
        [Description("Optional: the holder's name as the bank knows it, at most 70 characters.")] string? holderName = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the bank account's IBAN, BIC and holder");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "bank-statements", ct).ConfigureAwait(false);
            return await connections.SetAccountAsync(bankId, new BankAccountInput(iban, bic, holderName), Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "connect_bank_account", Title = "Start connecting a bank account", ReadOnly = false, Destructive = false)]
    [Description("Starts connecting a bank account to its bank through the company's open banking provider. Returns a link: give it to the user, who opens it in their browser, signs in at the bank and approves; the bank then sends them back to the ERP, which finishes the connection. Claude never signs in at a bank. Choose the bank with bank_accounts and a country first. Needs the Bank statements screen. Show the user the account and the bank and get an explicit yes; then confirm=true.")]
    public Task<object> ConnectBankAccountAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The bank account cost centre's id.")] Guid bankId,
        [Description("The bank's name exactly as bank_accounts with country lists it.")] string aspspName,
        [Description("The bank's two-letter country code.")] string country,
        [Description("business (default) or personal.")] string psuType = "business", CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the account to connect and its bank");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "bank-statements", ct).ConfigureAwait(false);
            var link = await connections.StartAsync(bankId, new BankConnectStart(aspspName, country, psuType), Registers.English, ct).ConfigureAwait(false);
            return new { link.Url, next = "Give the user this link to open in their own browser and sign in at the bank. The ERP finishes the connection when the bank sends them back." };
        });

    [McpServerTool(Name = "fetch_bank_statement", Title = "Fetch a bank statement from the bank", ReadOnly = false, Destructive = false)]
    [Description("Fetches a connected bank account's booked lines from its bank into a new bank statement, like an uploaded file: lines already held are skipped, pending ones come with a later fetch, and every line is then decided by hand (bank_statements, decide_statement_line). Returns the statement id, lines added and skipped, and the bank's balance next to the ledger's. Default period: from the last day fetched (or 30 days back) to today. Needs the Bank statements screen. Tell the user the account and period and get an explicit yes; then confirm=true.")]
    public Task<object> FetchBankStatementAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The connected bank account cost centre's id.")] Guid bankId,
        [Description("Optional: the first day, yyyy-MM-dd.")] DateOnly? from = null,
        [Description("Optional: the last day, yyyy-MM-dd; not after today.")] DateOnly? to = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the account and the period to fetch");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "bank-statements", ct).ConfigureAwait(false);
            return await feeds.FetchAsync(bankId, from, to, new PsuPresence(null, null), ct).ConfigureAwait(false);
        });
}

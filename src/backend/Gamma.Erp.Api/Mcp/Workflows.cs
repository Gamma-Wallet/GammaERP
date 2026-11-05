using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp;

/// <summary>
/// Named workflows (MCP prompts) for the main jobs in the ERP (the founder, 2026-11-04): each walks Claude through the tools in order, with the guide
/// pages to read and the points where the user must say yes. They describe how the ERP works today; the guide is the reference.
/// </summary>
[McpServerPromptType]
public static class Workflows
{
    private const string Always = """
        Work as the signed-in user, with their rights. Start with list_workplaces (and choose_workplace if needed). Before every changing tool, show the
        user exactly what will change and wait for an explicit yes; then call it with confirm=true. If a tool refuses, tell the user the ERP's reason.
        """;

    [McpServerPrompt(Name = "set_up_company", Title = "Set up a company")]
    [Description("Walks an administrator through setting up a company: details, places, users and roles, payment methods, point of sale, skin, and what is still missing.")]
    public static string SetUpCompany([Description("Optional: the company's name, or what kind of business it is.")] string? company = null) => $"""
        Help me set up {company ?? "our company"} in Gamma ERP. {Always}
        Read the guide first: getting-started/README.md, getting-started/add-a-company-to-a-group.md, company/cost-centres.md, users-and-rights/users.md,
        users-and-rights/roles.md, currencies-and-payments/payment-methods-by-cost-centre.md, point-of-sale/settings.md.
        Go step by step, asking me before each change:
        1. Is this a new company in our group? If so, add_company_to_group (country, name, activity profile, which configuration to load).
           A brand-new organisation can't be made here: it is made on the server (create-company).
        2. The company's details for the printed documents: set_company_details (trade name, tax number, address, phone, e-mail).
        3. Its places: warehouses, cash desks, banks, points of sale and their tables or tills (add_cost_center). Check what exists first (find_cost_centers).
        4. Users: add_user for each person, with the places they work at and their roles; each changes the first password at their first sign-in.
        5. Payment methods per place and where their money goes; point-of-sale settings (read_configuration, then save_configuration_row).
        6. The skin (list_skins, use_skin) if I want another look.
        7. Opening balances and stock come in through the Migration screen (system-settings/migration.md): tell me how, step by step.
        At the end, list what is still missing compared with the home page's "Finish your company" card and the guide.
        """;

    [McpServerPrompt(Name = "make_sales_invoice", Title = "Make and post a sales invoice")]
    [Description("Makes a sales invoice for a client, shows it, and posts it after a yes.")]
    public static string MakeSalesInvoice([Description("Who it is for and what is sold, in your own words.")] string what) => $"""
        Make a sales invoice: {what}. {Always}
        Read documents/README.md and documents/posting.md if needed. Find the client (find_partners) and the products (find_products); take the invoice type
        from document_types. Save it as a draft (save_draft_document) and show me the lines, VAT and total. Post it (post_document) only after my yes: posting
        is final and is corrected only by a reversal.
        """;

    [McpServerPrompt(Name = "pay_invoices", Title = "Record a payment against invoices")]
    [Description("Records a payment received or made, covering open invoices chosen by their numbers.")]
    public static string PayInvoices([Description("Who paid or was paid, how much, and how (cash, bank).")] string what) => $"""
        Record this payment: {what}. {Always}
        Read documents/payments-and-open-invoices.md. A payment covers open invoices chosen by hand, by invoice number (never first-in-first-out).
        Show me the open invoices (open_items), agree with me which ones it covers, save the payment as a draft, and post it after my yes.
        """;

    [McpServerPrompt(Name = "bank_statement", Title = "Work through a bank statement")]
    [Description("Explains how a bank statement is matched to documents in the ERP and helps with the open lines.")]
    public static string BankStatement() => $"""
        Help me with the bank statement. {Always}
        Read documents/bank-statements.md and currencies-and-payments/bank-statement-formats.md. The statement is imported on its screen; for its lines,
        find the matching documents (list_documents, open_items) and tell me what each line matches or what document is missing.
        """;

    [McpServerPrompt(Name = "monthly_invoice_run", Title = "Run the contracts' invoices")]
    [Description("Checks the contracts due and runs their invoices, by hand.")]
    public static string MonthlyInvoiceRun([Description("Up to which day (default: today).")] string? until = null) => $"""
        Run the contracts' invoices due up to {until ?? "today"}. {Always}
        Read contracts/invoice-run.md and contracts/billing-plan.md. Only posted contracts make invoices. List the contracts due, show me the invoices
        that would be made, and make them only after my yes. Nothing runs by itself in the ERP.
        """;

    [McpServerPrompt(Name = "close_month", Title = "Close a month")]
    [Description("Checks a month before closing it: drafts left, unbalanced scenarios, depreciation, VAT; then closes the period after a yes.")]
    public static string CloseMonth([Description("The month, e.g. 2026-10.")] string month) => $"""
        Help me close {month}. {Always}
        Read accounting/closed-periods.md, accounting/unbalanced-scenarios.md and the fixed assets depreciation pages. Check: drafts left in the month
        (list_documents), the trial balance (trial_balance), the VAT return (vat_return). Tell me what is open before closing; close the period only after
        my yes (the closed-periods configuration: read_configuration, then save_configuration_row).
        """;

    [McpServerPrompt(Name = "close_year", Title = "Close a year")]
    [Description("Checks a fiscal year and closes it, optionally changing the company's currency from the next year.")]
    public static string CloseYear([Description("The year, e.g. 2026.")] string year) => $"""
        Help me close the year {year}. {Always}
        Read accounting/year-closing.md. Check drafts left, the trial balance and the balance sheet (balance_sheet, profit_and_loss). If the company's
        currency changes from the next year, the rate of the next year's first day must be set first. Close only after my yes.
        """;

    [McpServerPrompt(Name = "update_exchange_rates", Title = "Update exchange rates")]
    [Description("Gets exchange rates from a source the user names and saves them in the ERP after a yes.")]
    public static string UpdateExchangeRates([Description("Which currencies and day, and where to take the rates from (e.g. the central bank's site).")] string what) => $"""
        Update exchange rates: {what}. {Always}
        Read currencies-and-payments/currencies-and-rates.md. The ERP doesn't fetch rates: get them from the source I name (ask me if I didn't), then see
        which currencies the company has (list_currencies). A rate is one unit of the currency in the company's own currency: convert if the source quotes
        it the other way. Show me the table of rates you will save; save them with set_exchange_rates after my yes.
        """;
}

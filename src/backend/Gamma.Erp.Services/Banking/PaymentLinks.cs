using System.Security.Cryptography;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>What the payment link page shows: no personal data, only the shop, the amount, the bills' codes, the state and how it can be paid.</summary>
/// <para>BillPaid: the ERP has paid the bill (the confirmation did, or the cashier). Note: why the confirmation could not pay it by itself.</para>
public sealed record PaymentLinkView(string PayToken, string Shop, decimal Amount, string Currency, string Reference, string Status, DateTimeOffset ExpiresOn,
    IReadOnlyList<string> Methods, string? ProviderReference, string? GammaBusinessId = null, bool BillPaid = false, string? Note = null, bool FromApp = false);

/// <summary>The till's view of its payment link: Open / Paid / Expired, the payment the confirmation posted (when it did) and why not; Gamma's link and errors.</summary>
public sealed record TillLinkDto(string PayToken, string Status, string? Method, Guid? PaymentId, string? SettleError, string? GammaLink, string? GammaErrors);

/// <summary>What the bank's return came to: the link (when known) and what to tell the payer.</summary>
public sealed record PaymentReturn(PaymentLinkView? Link, bool Paid, string Message);

/// <summary>
/// The till's payment links (the founder, 2026-11-18; plan 4.5): a payment method whose <see cref="PaymentMethod.ProviderKind"/> is BankOfCyprus makes the
/// till show <c>&lt;erp&gt;/pay/{payToken}</c> as its QR code. The page pays through the company's Bank of Cyprus provider: the ERP starts the payment, the
/// payer approves it at the bank, the bank sends them back to <c>/pay/bank-return</c>, and the ERP completes it and writes the bank notice. The till's
/// Bank confirmed shows it. Nothing is posted here: the method's PaysOnConfirmation lets the API pay the bill on the confirmation (PaymentLinkSettlement),
/// else the cashier presses Paid. Gamma Wallet opens <c>/pay/{payToken}/start?method=card|bank&amp;app=true</c> for the way the customer chose.
/// <para>The page and the return have nobody signed in: they read the link by its token only, and write rows with the link's own corporate and company.</para>
/// </summary>
public sealed class PaymentLinks(ErpDbContext db, ISecretBox secrets, BankOfCyprusConnector boc, JccConnector jcc, TimeProvider? clock = null)
{
    public const string BankOfCyprus = BankOfCyprusConnector.KindName;

    public const string Jcc = JccConnector.KindName;

    /// <summary>"Pay with Gamma app" (plan 10): the bill is sent open to Gamma; the customer pays it from their own bank in Gamma Wallet; the bank's notice of
    /// the money on the shop's account pays it here.</summary>
    public const string GammaApp = "GammaApp";

    /// <summary>The provider kinds a payment method can pay through by a link (a payment request the ERP keeps).</summary>
    public static bool ByLink(string? kind) => kind is BankOfCyprus or Jcc or GammaApp;

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>
    /// The open link for this payment at the till (the same one while the bill and amount stay the same and it has not expired), or a new one. The caller has
    /// checked the point of sale, the method, the bank and the amount.
    /// </summary>
    public async Task<(PaymentLink Link, string Url)> ForTillAsync(Guid tenantId, Guid company, Guid pointOfSaleId, Guid paymentMethodId, Guid bankId, decimal amount,
        string currency, string reference, string kind, CancellationToken ct)
    {
        var provider = await db.Set<BankProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        Uri? back = null;
        if (kind == GammaApp)
        {
            // Nobody opens this link: Gamma's own bill is the QR. The request lives as long as a payment link does.
        }
        else if (kind == Jcc)
        {
            var card = await db.Set<CardProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
            if (card is null || !Uri.TryCreate(card.ReturnUrl, UriKind.Absolute, out back))
            {
                throw ValidationException.For("paymentMethodId", $"{ErrorCodes.NotConfiguredError}: this method takes cards through JCC: set the company's card gateway (Configuration › Open banking, Card payments).");
            }
        }
        else if (provider is not { Kind: BankOfCyprus, PaymentsEnabled: true } || !Uri.TryCreate(provider.PaymentReturnUrl, UriKind.Absolute, out back))
        {
            throw ValidationException.For("paymentMethodId", $"{ErrorCodes.NotConfiguredError}: this method pays through Bank of Cyprus: set the company's provider to Bank of Cyprus with payments on and their return address (Configuration › Open banking).");
        }

        var now = time.GetUtcNow();
        var link = await db.Set<PaymentLink>().AsNoTracking().FirstOrDefaultAsync(l => l.Active && l.OwnerId == company && l.Status == "Open" && l.PointOfSaleId == pointOfSaleId
            && l.PaymentMethodId == paymentMethodId && l.Reference == reference && l.Amount == amount && l.ExpiresOn > now, ct).ConfigureAwait(false);
        if (link is null)
        {
            link = new PaymentLink
            {
                TenantId = tenantId, OwnerId = company, PayToken = NewToken(), PointOfSaleId = pointOfSaleId, PaymentMethodId = paymentMethodId, BankId = bankId, Amount = amount,
                Currency = currency, Reference = reference, Method = kind, ExpiresOn = now.AddMinutes(Math.Clamp(provider?.PaymentLinkMinutes ?? 20, 1, 24 * 60)),
            };
            db.Add(link);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return (link, back is null ? string.Empty : $"{back.GetLeftPart(UriPartial.Authority)}/pay/{link.PayToken}");
    }

    /// <summary>Keeps what Gamma answered for the open bill ("Pay with Gamma app"), or why it did not take it.</summary>
    public async Task KeepGammaAsync(Guid linkId, GammaBill? bill, string? error, CancellationToken ct)
    {
        var rows = db.Set<PaymentLink>().Where(l => l.Id == linkId);
        if (bill is null)
        {
            await rows.ExecuteUpdateAsync(u => u.SetProperty(l => l.GammaErrors, error), ct).ConfigureAwait(false);
            return;
        }

        var (id, code, link) = (bill.BillId, bill.Code, bill.Link);
        await rows.ExecuteUpdateAsync(u => u.SetProperty(l => l.GammaBillId, id).SetProperty(l => l.GammaBillCode, code).SetProperty(l => l.GammaLink, link)
            .SetProperty(l => l.GammaErrors, (string?)null), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A bank notice just kept (money on one of the company's accounts): when it is the payment of an open "Pay with Gamma app" request — same bank
    /// account, same amount, its text naming every bill of the request — the request is paid by it. Answers the request's token (then
    /// PaymentLinkSettlement pays the bills), or null. Nothing is guessed: no match, nothing happens and the notice waits for a person.
    /// </summary>
    public async Task<string?> MatchNoticeAsync(Guid noticeId, CancellationToken ct)
    {
        var notice = await db.Set<BankNotice>().AsNoTracking().FirstOrDefaultAsync(n => n.Active && n.Id == noticeId && n.Kind == "bank", ct).ConfigureAwait(false);
        if (notice?.Reference is not { Length: > 0 } text)
        {
            return null;
        }

        var words = text.Split([' ', ',', ';', '/', '\t'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = await db.Set<PaymentLink>().Where(l => l.Active && l.OwnerId == notice.OwnerId && l.Method == GammaApp && l.BankNoticeId == null
            && (l.Status == "Open" || l.Status == "Expired") && l.BankId == notice.BankId && l.Amount == notice.Amount).OrderByDescending(l => l.CreatedOn).ToListAsync(ct)
            .ConfigureAwait(false);
        var link = candidates.FirstOrDefault(l => l.Reference.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(words.Contains));
        if (link is null)
        {
            return null;
        }

        (link.Status, link.ProviderReference, link.PaidOn, link.BankNoticeId) = ("Paid", notice.ProviderReference, time.GetUtcNow(), notice.Id);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return link.PayToken;
    }

    /// <summary>The till's view of its link (signed in, the company's own): state, whether the bill is paid here and by which payment, and why not.</summary>
    public async Task<TillLinkDto?> TillStatusAsync(Guid company, string payToken, CancellationToken ct)
    {
        var link = await db.Set<PaymentLink>().AsNoTracking().FirstOrDefaultAsync(l => l.Active && l.OwnerId == company && l.PayToken == payToken, ct).ConfigureAwait(false);
        return link is null ? null : new TillLinkDto(link.PayToken, link.Status, link.Method, link.PaymentId, link.SettleError, link.GammaLink, link.GammaErrors);
    }

    /// <summary>The page's view of a link; null when there is no such link.</summary>
    public async Task<PaymentLinkView?> ViewAsync(string payToken, CancellationToken ct)
    {
        var link = await LinkAsync(payToken, ct).ConfigureAwait(false);
        return link is null ? null : await ViewOfAsync(link, ct).ConfigureAwait(false);
    }

    /// <summary>Gamma Wallet started this link's payment: its return pages send the payer back into the app.</summary>
    public async Task FromAppAsync(string payToken, CancellationToken ct)
    {
        await db.Set<PaymentLink>().Where(l => l.Active && l.PayToken == payToken && l.Status == "Open").ExecuteUpdateAsync(u => u.SetProperty(l => l.FromApp, true), ct)
            .ConfigureAwait(false);
    }

    /// <summary>"Pay with Bank of Cyprus": starts the payment at the bank and gives the address where the payer approves it.</summary>
    public async Task<string> StartBankOfCyprusAsync(string payToken, CancellationToken ct)
    {
        var link = await db.Set<PaymentLink>().FirstOrDefaultAsync(l => l.Active && l.PayToken == payToken, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("payToken", "There is no such payment link.");
        Expire(link);
        await ChooseAsync(link, BankOfCyprus, ct).ConfigureAwait(false);
        Payable(link, BankOfCyprus);
        var (provider, secret, account) = await PaymentSideAsync(link, ct).ConfigureAwait(false);
        var paymentId = await BankConnections.Call(() => boc.InitiatePaymentAsync(provider, secret, account, decimal.Round(link.Amount, 2), link.Currency, link.Reference,
            "GAMMA-" + link.PayToken[..16], ct)).ConfigureAwait(false);
        (link.ProviderPaymentId, link.Method) = (paymentId, BankOfCyprus);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return BankOfCyprusConnector.ApprovalUrl(provider, paymentId, provider.PaymentReturnUrl!, link.PayToken);
    }

    /// <summary>
    /// The bank sends the payer back here with the state (the link's token) and a code, or an error when they declined. The approved payment is completed;
    /// done (CPLT), the link is paid and the bank notice is written under the bank's own id of the statement line, so Fetch from bank recognises it.
    /// </summary>
    public async Task<PaymentReturn> ReturnAsync(string? state, string? code, string? error, CancellationToken ct)
    {
        var link = string.IsNullOrEmpty(state) ? null : await db.Set<PaymentLink>().FirstOrDefaultAsync(l => l.Active && l.PayToken == state, ct).ConfigureAwait(false);
        if (link is null)
        {
            return new(null, false, "This payment is not known here.");
        }

        if (link.Status == "Paid")
        {
            return new(await ViewOfAsync(link, ct).ConfigureAwait(false), true, "Paid. Show this page at the till.");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || link.ProviderPaymentId is null)
        {
            return new(await ViewOfAsync(link, ct).ConfigureAwait(false), false, "The payment was not approved at the bank. Nothing was paid; you can try again.");
        }

        var (provider, secret, _) = await PaymentSideAsync(link, ct).ConfigureAwait(false);
        string status;
        string? reference;
        try
        {
            (status, reference) = await boc.ExecutePaymentAsync(provider, secret, code, provider.PaymentReturnUrl!, link.ProviderPaymentId, ct).ConfigureAwait(false);
        }
        catch (BankProviderException e)
        {
            return new(await ViewOfAsync(link, ct).ConfigureAwait(false), false, "The bank did not complete the payment: " + e.Message);
        }

        if (status != "CPLT")
        {
            return new(await ViewOfAsync(link, ct).ConfigureAwait(false), false, $"The bank did not complete the payment ({status}). Nothing was paid.");
        }

        var now = time.GetUtcNow();
        (link.Status, link.ProviderReference, link.PaidOn) = ("Paid", reference, now);
        link.BankNoticeId = await NoticeAsync(link, provider, reference, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new(await ViewOfAsync(link, ct).ConfigureAwait(false), true, "Paid. Show this page at the till.");
    }

    // ------------------------------------------------------------------ cards (JCC)

    /// <summary>"Pay by card": registers the order at JCC and gives its card page. Each try is its own order (JCC refuses an order number twice).</summary>
    public async Task<string> StartCardAsync(string payToken, CancellationToken ct)
    {
        var link = await db.Set<PaymentLink>().FirstOrDefaultAsync(l => l.Active && l.PayToken == payToken, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("payToken", "There is no such payment link.");
        Expire(link);
        await ChooseAsync(link, Jcc, ct).ConfigureAwait(false);
        Payable(link, Jcc);
        var (card, password) = await CardSideAsync(link.OwnerId, ct).ConfigureAwait(false);
        var minor = (long)decimal.Round(link.Amount * 100m, 0, MidpointRounding.AwayFromZero);
        var orderNumber = $"{Cut(link.Reference, 20)}-{NewToken()[..8]}";
        var callback = $"{new Uri(card.ReturnUrl).GetLeftPart(UriPartial.Authority)}/api/card-callbacks/{card.Id}";
        var (orderId, formUrl) = await BankConnections.Call(() => jcc.RegisterAsync(card, password, minor, "978", orderNumber, card.ReturnUrl, callback, link.Reference, ct))
            .ConfigureAwait(false);
        (link.ProviderPaymentId, link.Method) = (orderId, Jcc);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return formUrl;
    }

    /// <summary>JCC sends the payer back with the order id: its state is read; held or deposited, the link is paid and the card notice written.</summary>
    public async Task<PaymentReturn> CardReturnAsync(string? orderId, CancellationToken ct)
    {
        var link = string.IsNullOrEmpty(orderId) ? null : await db.Set<PaymentLink>().FirstOrDefaultAsync(l => l.Active && l.ProviderPaymentId == orderId && l.Method == Jcc, ct)
            .ConfigureAwait(false);
        if (link is null)
        {
            return new(null, false, "This payment is not known here.");
        }

        if (link.Status != "Paid")
        {
            var (card, password) = await CardSideAsync(link.OwnerId, ct).ConfigureAwait(false);
            JccOrderStatus status;
            try
            {
                status = await jcc.StatusAsync(card, password, orderId!, ct).ConfigureAwait(false);
            }
            catch (BankProviderException e)
            {
                return new(await ViewOfAsync(link, ct).ConfigureAwait(false), false, "The card payment can't be checked now: " + e.Message);
            }

            if (!status.Paid)
            {
                return new(await ViewOfAsync(link, ct).ConfigureAwait(false), false,
                    $"The card was not charged ({status.ActionText ?? "declined"}). Nothing was paid; you can try again.");
            }

            await CardPaidAsync(link, card, status, ct).ConfigureAwait(false);
        }

        return new(await ViewOfAsync(link, ct).ConfigureAwait(false), true, "Paid by card. Show this page at the till.");
    }

    /// <summary>
    /// JCC's callback (no sign-in): the checksum must be the merchant's key's. An approved or deposited order of one of the company's links is read back
    /// from JCC and becomes the card notice; anything else is acknowledged and changes nothing. The answer is the HTTP status to give.
    /// </summary>
    public async Task<int> CardCallbackAsync(Guid cardProviderId, IReadOnlyDictionary<string, string> parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var card = await db.Set<CardProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.Id == cardProviderId, ct).ConfigureAwait(false);
        if (card?.CallbackKey is not { Length: > 0 } sealedKey || secrets.Open(sealedKey) is not { } key)
        {
            return 404;
        }

        if (!JccConnector.ChecksumMatches(parameters, key, parameters.GetValueOrDefault("checksum")))
        {
            return 401;
        }

        var orderId = parameters.GetValueOrDefault("mdOrder");
        var operation = parameters.GetValueOrDefault("operation");
        if (parameters.GetValueOrDefault("status") != "1" || operation is not ("approved" or "deposited") || string.IsNullOrEmpty(orderId))
        {
            return 200;
        }

        var link = await db.Set<PaymentLink>().FirstOrDefaultAsync(l => l.Active && l.OwnerId == card.OwnerId && l.ProviderPaymentId == orderId && l.Method == Jcc, ct)
            .ConfigureAwait(false);
        if (link is null || link.Status == "Paid")
        {
            return 200;
        }

        // The callback says what happened; the amount and the card come from JCC itself.
        if (secrets.Open(card.Password) is not { } password)
        {
            return 503;
        }

        var status = await jcc.StatusAsync(card, password, orderId, ct).ConfigureAwait(false);
        if (status.Paid)
        {
            await CardPaidAsync(link, card, status, ct).ConfigureAwait(false);
        }

        return 200;
    }

    /// <summary>Check card at the till (a person's press): the newest link of this payment that went to JCC is read from it; paid, the notice is written.</summary>
    public async Task CheckCardAsync(Guid company, Guid pointOfSaleId, Guid paymentMethodId, decimal amount, string reference, CancellationToken ct)
    {
        var link = await db.Set<PaymentLink>().Where(l => l.Active && l.OwnerId == company && l.PointOfSaleId == pointOfSaleId && l.PaymentMethodId == paymentMethodId
                && l.Reference == reference && l.Amount == amount && l.Method == Jcc && l.ProviderPaymentId != null)
            .OrderByDescending(l => l.CreatedOn).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (link is null || link.Status == "Paid")
        {
            return;
        }

        var (card, password) = await CardSideAsync(company, ct).ConfigureAwait(false);
        var status = await BankConnections.Call(() => jcc.StatusAsync(card, password, link.ProviderPaymentId!, ct)).ConfigureAwait(false);
        if (status.Paid)
        {
            await CardPaidAsync(link, card, status, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gives money back to the card of a paid link (a person's press; the receipt is then reversed as usual): up to what is left. JCC's externalRefundId
    /// makes a retry of the same press safe.
    /// </summary>
    public async Task<PaymentLinkView> RefundAsync(Guid company, string payToken, decimal amount, CancellationToken ct)
    {
        var link = await db.Set<PaymentLink>().FirstOrDefaultAsync(l => l.Active && l.OwnerId == company && l.PayToken == payToken, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("payToken", "There is no such payment link.");
        if (link.Method != Jcc || link.Status is not ("Paid" or "Refunded") || link.ProviderPaymentId is null)
        {
            throw ValidationException.For("payToken", "Only a card payment that was paid can be refunded.");
        }

        var left = decimal.Round(link.Amount, 2) - link.RefundedAmount;
        if (amount <= 0 || decimal.Round(amount, 2) != amount || amount > left)
        {
            throw ValidationException.For("amount", $"Refund above 0, in cents, at most {left:0.00}.");
        }

        var (card, password) = await CardSideAsync(company, ct).ConfigureAwait(false);
        var refundId = $"{link.PayToken[..12]}-{link.RefundedAmount + amount:0.00}";
        var orderId = link.ProviderPaymentId;
        await BankConnections.Call(async () =>
        {
            await jcc.RefundAsync(card, password, orderId, (long)(amount * 100m), refundId, ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
        link.RefundedAmount += amount;
        if (link.RefundedAmount >= decimal.Round(link.Amount, 2))
        {
            link.Status = "Refunded";
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ViewOfAsync(link, ct).ConfigureAwait(false);
    }

    /// <summary>A link paid by card: Paid, and its card notice (one per JCC order) for the till's Card paid.</summary>
    private async Task CardPaidAsync(PaymentLink link, CardProvider card, JccOrderStatus status, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        (link.Status, link.ProviderReference, link.PaidOn) = ("Paid", status.ApprovalCode, now);
        var notice = await db.Set<BankNotice>().FirstOrDefaultAsync(n => n.Active && n.CardProviderId == card.Id && n.ExternalId == link.ProviderPaymentId, ct)
            .ConfigureAwait(false);
        if (notice is null)
        {
            notice = new BankNotice
            {
                TenantId = link.TenantId, OwnerId = link.OwnerId, Kind = "card", CardProviderId = card.Id, BankId = link.BankId, ExternalId = link.ProviderPaymentId!,
                MessageId = status.OrderNumber, Amount = status.Amount / 100m, Currency = link.Currency, Reference = link.Reference, ProviderReference = status.ApprovalCode,
                PayerName = status.MaskedPan is { } pan ? "Card " + pan : "Card", Booked = true, BookedOn = DateOnly.FromDateTime(now.UtcDateTime), ReceivedOn = now,
            };
            db.Add(notice);
        }

        link.BankNoticeId = notice.Id;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The callback and the payer's return at once: the unique index kept the first notice.
        }
    }

    /// <summary>The company's JCC gateway and its opened password.</summary>
    private async Task<(CardProvider Card, string Password)> CardSideAsync(Guid? company, CancellationToken ct)
    {
        var card = await db.Set<CardProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("payToken", "The shop can't take card payments now: pay at the till another way.");
        var password = secrets.Open(card.Password) ?? throw ValidationException.For("payToken", "The shop's card settings need attention: pay at the till another way.");
        return (card, password);
    }

    /// <summary>
    /// The ways the link can be paid now (the founder, 2026-11-19: Gamma Wallet chooses card or bank before it scans): each provider kind that a payment
    /// method linked to the till's point of sale (with its bank or cash account) pays through, and whose provider the company has set.
    /// </summary>
    private async Task<IReadOnlyList<(string Kind, Guid MethodId, Guid BankId)>> WaysAsync(PaymentLink link, CancellationToken ct)
    {
        var linked = await (from l in db.Set<InterPaymentMethodCostCenter>().AsNoTracking()
                            join m in db.Set<PaymentMethod>().AsNoTracking() on l.PaymentMethodId equals m.Id
                            where l.Active && m.Active && l.CostCenterId == link.PointOfSaleId && l.CashBankId != null && (m.ProviderKind == BankOfCyprus || m.ProviderKind == Jcc)
                            select new { m.Id, m.ProviderKind, Bank = l.CashBankId!.Value, m.Code }).ToListAsync(ct).ConfigureAwait(false);
        var bank = await db.Set<BankProvider>().AsNoTracking().AnyAsync(p => p.Active && p.OwnerId == link.OwnerId && p.Kind == BankOfCyprus && p.PaymentsEnabled, ct)
            .ConfigureAwait(false);
        var card = await db.Set<CardProvider>().AsNoTracking().AnyAsync(p => p.Active && p.OwnerId == link.OwnerId, ct).ConfigureAwait(false);
        // The link's own method first; then one method per kind.
        return [.. linked.Where(m => m.ProviderKind == BankOfCyprus ? bank : card).OrderBy(m => m.Id == link.PaymentMethodId ? 0 : 1).ThenBy(m => m.Code, StringComparer.Ordinal)
            .GroupBy(m => m.ProviderKind!).Select(g => (g.Key, g.First().Id, g.First().Bank))];
    }

    /// <summary>The payer chose another way than the till's method: the link now pays by the company's method of that kind at the till.</summary>
    private async Task ChooseAsync(PaymentLink link, string kind, CancellationToken ct)
    {
        if (link.Status != "Open" || (link.Method ?? BankOfCyprus) == kind)
        {
            return;
        }

        var way = (await WaysAsync(link, ct).ConfigureAwait(false)).FirstOrDefault(w => w.Kind == kind);
        if (way.Kind is null)
        {
            throw ValidationException.For("payToken", kind == Jcc ? "The shop does not take cards this way: pay with your bank or at the till." : "The shop does not take bank payments this way: pay by card or at the till.");
        }

        (link.Method, link.PaymentMethodId, link.BankId, link.ProviderPaymentId) = (kind, way.MethodId, way.BankId, null);
    }

    /// <summary>Refuses a link that can't be paid now, or not this way.</summary>
    private static void Payable(PaymentLink link, string kind)
    {
        if (link.Status != "Open")
        {
            throw ValidationException.For("payToken", link.Status == "Paid" ? "This bill is paid already." : "This payment link can no longer be paid: ask at the till for a new one.");
        }

        if ((link.Method ?? BankOfCyprus) != kind)
        {
            throw ValidationException.For("payToken", "This payment link is not paid this way.");
        }
    }

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;

    /// <summary>
    /// The bank notice of the done payment: the one the bank's own message wrote already (same provider reference), or a new one whose id is the statement
    /// line's own (one read of today's statement), or, when the line can't be found yet, "BOC-" and the bank's reference.
    /// </summary>
    private async Task<Guid> NoticeAsync(PaymentLink link, BankProvider provider, string? reference, CancellationToken ct)
    {
        if (reference is { Length: > 0 } && await db.Set<BankNotice>().FirstOrDefaultAsync(n => n.Active && n.ProviderId == provider.Id && n.ProviderReference == reference, ct)
                .ConfigureAwait(false) is { } held)
        {
            return held.Id;
        }

        var connection = await db.Set<BankConnection>().AsNoTracking().FirstAsync(c => c.Active && c.BankId == link.BankId && c.OwnerId == link.OwnerId, ct).ConfigureAwait(false);
        string? lineId = null;
        DateOnly booked = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (secrets.Open(provider.PrivateKey) is { } secret && connection.AccountUid is { } uid)
        {
            try
            {
                var page = await boc.TransactionsAsync(provider, secret, uid, booked.AddDays(-1), booked, null, new PsuPresence(null, null), ct).ConfigureAwait(false);
                var line = page.Lines.LastOrDefault(l => l.Amount == decimal.Round(link.Amount, 2) && (l.Text?.Contains(link.Reference, StringComparison.OrdinalIgnoreCase) ?? false));
                (lineId, booked) = line is null ? (null, booked) : (line.ExternalId, line.Date);
            }
            catch (BankProviderException)
            {
                // The statement could not be read now: the notice keeps the bank's reference.
            }
        }

        var externalId = lineId ?? "BOC-" + (reference ?? link.ProviderPaymentId);
        if (await db.Set<BankNotice>().FirstOrDefaultAsync(n => n.Active && n.ProviderId == provider.Id && n.ExternalId == externalId, ct).ConfigureAwait(false) is { } same)
        {
            return same.Id;
        }

        var notice = new BankNotice
        {
            TenantId = link.TenantId, OwnerId = link.OwnerId, Kind = "bank", ProviderId = provider.Id, BankId = link.BankId, ExternalId = externalId,
            MessageId = link.ProviderPaymentId, Amount = decimal.Round(link.Amount, 2), Currency = link.Currency, Reference = link.Reference, ProviderReference = reference,
            Booked = lineId is not null, BookedOn = booked, ReceivedOn = time.GetUtcNow(),
        };
        db.Add(notice);
        return notice.Id;
    }

    /// <summary>The company's Bank of Cyprus provider with payments on, its opened secret, and the bank account id the money goes to.</summary>
    private async Task<(BankProvider Provider, string Secret, string Account)> PaymentSideAsync(PaymentLink link, CancellationToken ct)
    {
        var provider = await db.Set<BankProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == link.OwnerId, ct).ConfigureAwait(false);
        if (provider is not { Kind: BankOfCyprus, PaymentsEnabled: true } || string.IsNullOrEmpty(provider.PaymentReturnUrl))
        {
            throw ValidationException.For("payToken", "The shop can't take Bank of Cyprus payments now: pay at the till another way.");
        }

        var secret = secrets.Open(provider.PrivateKey) ?? throw ValidationException.For("payToken", "The shop's bank settings need attention: pay at the till another way.");
        var connection = await db.Set<BankConnection>().AsNoTracking().FirstOrDefaultAsync(c => c.Active && c.BankId == link.BankId && c.OwnerId == link.OwnerId
            && c.ProviderId == provider.Id && c.AccountUid != null, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("payToken", "The shop's bank account is not connected to Bank of Cyprus: pay at the till another way.");
        return (provider, secret, BankOfCyprusConnector.AccountOf(connection.AccountUid!));
    }

    private async Task<PaymentLink?> LinkAsync(string payToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payToken) || payToken.Length > 64)
        {
            return null;
        }

        // Read afresh: the confirmation may have paid the bill since (PaymentLinkSettlement writes in its own scope).
        foreach (var held in db.ChangeTracker.Entries<PaymentLink>().Where(e => e.Entity.PayToken == payToken).ToList())
        {
            held.State = EntityState.Detached;
        }

        var link = await db.Set<PaymentLink>().FirstOrDefaultAsync(l => l.Active && l.PayToken == payToken, ct).ConfigureAwait(false);
        if (link is not null && Expire(link))
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return link;
    }

    /// <summary>An open link past its time becomes Expired; true when it changed.</summary>
    private bool Expire(PaymentLink link)
    {
        if (link.Status == "Open" && link.ExpiresOn <= time.GetUtcNow())
        {
            link.Status = "Expired";
            return true;
        }

        return false;
    }

    private async Task<PaymentLinkView> ViewOfAsync(PaymentLink link, CancellationToken ct)
    {
        var company = await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == link.OwnerId, ct).ConfigureAwait(false);
        var shop = company?.Name.In(Models.Base.TranslatedText.DefaultLanguage) ?? company?.Code ?? "Shop";
        IReadOnlyList<string> methods = link.Status == "Open" ? [.. (await WaysAsync(link, ct).ConfigureAwait(false)).Select(w => w.Kind)] : [];
        if (link.Status == "Open" && methods.Count == 0)
        {
            methods = [link.Method ?? BankOfCyprus];
        }

        // The company's Gamma business, so Gamma Wallet can say whom the customer is paying (plan 4.5).
        var gamma = await db.Set<WalletProvider>().AsNoTracking().Where(p => p.Active && p.OwnerId == link.OwnerId).Select(p => p.GammaBusinessId).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return new PaymentLinkView(link.PayToken, shop, decimal.Round(link.Amount, 2), link.Currency, link.Reference, link.Status, link.ExpiresOn, methods, link.ProviderReference, gamma,
            link.PaymentId is not null, link.SettleError, link.FromApp);
    }

    /// <summary>24 random bytes, URL-safe (32 characters): the link can't be guessed.</summary>
    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

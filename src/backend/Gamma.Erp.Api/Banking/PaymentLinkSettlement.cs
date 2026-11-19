using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Banking;
using Gamma.Erp.Services.Pos;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Api.Banking;

/// <summary>
/// The confirmation pays the bill (the founder, 2026-11-19: "the cashier should not press Paid, the confirmation comes from the bank or the card
/// institution"). When a payment link is paid at the bank or by card and its payment method has <see cref="PaymentMethod.PaysOnConfirmation"/>, the ERP
/// does what the till's Paid does, as the cashier who made the link, at its till: posts the payment for the link's bills, keeps the bank line, declares
/// the bills to the tax authority and sends the bill to Gamma Wallet.
/// <para>A link is paid this way once: <see cref="PaymentLink.SettledOn"/> is claimed first, so the bank's or gateway's call and the payer's return at the same
/// time never pay twice. When it can't (the bills were paid at the till already, the amount changed), the reason is kept on the link and the cashier
/// presses Paid as before.</para>
/// </summary>
public sealed partial class PaymentLinkSettlement(IServiceScopeFactory scopes, ILogger<PaymentLinkSettlement> log)
{
    /// <summary>Settles the link with this token, when it is paid and not settled yet.</summary>
    public Task SettleTokenAsync(string? payToken, CancellationToken ct) =>
        string.IsNullOrEmpty(payToken) ? Task.CompletedTask : SettleAsync(l => l.PayToken == payToken, ct);

    /// <summary>Settles the link of this card order (JCC's order id), when it is paid and not settled yet.</summary>
    public Task SettleOrderAsync(string? orderId, CancellationToken ct) =>
        string.IsNullOrEmpty(orderId) ? Task.CompletedTask : SettleAsync(l => l.ProviderPaymentId == orderId && l.Method == PaymentLinks.Jcc, ct);

    private async Task SettleAsync(System.Linq.Expressions.Expression<Func<PaymentLink, bool>> which, CancellationToken ct)
    {
        PaymentLink? link;
        // 1. Nobody signed in: the link is read and claimed by its own row.
        await using (var owner = scopes.CreateAsyncScope())
        {
            var db = owner.ServiceProvider.GetRequiredService<ErpDbContext>();
            link = await db.Set<PaymentLink>().AsNoTracking().Where(l => l.Active && l.Status == "Paid" && l.SettledOn == null && l.BankNoticeId != null).Where(which)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (link is null)
            {
                return;
            }

            var paysItself = await db.Set<PaymentMethod>().AsNoTracking().AnyAsync(m => m.Id == link.PaymentMethodId && m.Active && m.PaysOnConfirmation, ct).ConfigureAwait(false);
            if (!paysItself)
            {
                return;
            }

            var now = TimeProvider.System.GetUtcNow();
            var claimed = await db.Set<PaymentLink>().Where(l => l.Id == link.Id && l.SettledOn == null).ExecuteUpdateAsync(u => u.SetProperty(l => l.SettledOn, now), ct)
                .ConfigureAwait(false);
            if (claimed == 0)
            {
                return;
            }
        }

        // 2. As the cashier who made the link, at its till, in the link's corporate.
        Guid? paymentId = null;
        string? error = null;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ErpDbContext>();
            try
            {
                if (link.CreatedBy is not { } user)
                {
                    throw ValidationException.For("payToken", "The payment link names no cashier: press Paid at the till.");
                }

                sp.GetRequiredService<TenantContext>().Set(link.TenantId, user);
                var roles = await (from l in db.Set<InterUserCategory>().AsNoTracking() join c in db.Set<UserCategory>().AsNoTracking() on l.CategoryId equals c.Id
                                   where l.ElementId == user select new { c.Id, c.IsAdministrator }).ToListAsync(ct).ConfigureAwait(false);
                var nodes = await db.Set<InterUserCostCenter>().AsNoTracking().Where(l => l.UserId == user && l.CostCenterId != null).Select(l => l.CostCenterId!.Value)
                    .ToListAsync(ct).ConfigureAwait(false);
                // The company (its bills are its rows; a node sees only itself and below), else the till itself.
                var node = link.OwnerId is { } company && await Auth.Workplaces.MayWorkAtAsync(db, nodes, company, ct).ConfigureAwait(false) ? company
                    : await Auth.Workplaces.MayWorkAtAsync(db, nodes, link.PointOfSaleId, ct).ConfigureAwait(false) ? link.PointOfSaleId
                    : throw ValidationException.For("payToken", "The cashier who made the payment link no longer works at this till: press Paid at the till.");
                sp.GetRequiredService<ActingContext>().Current = new RequestContext(link.TenantId, user, node, roles.Select(r => r.Id).ToHashSet(), roles.Any(r => r.IsAdministrator));

                var pos = sp.GetRequiredService<PosService>();
                var settings = await pos.SettingsAsync(link.PointOfSaleId, "en", ct).ConfigureAwait(false);
                if (settings.BillTypeId is not { } billType || settings.MoneyInTypeId is not { } moneyIn || settings.CashDeskId is not { } cashDesk)
                {
                    throw ValidationException.For("pointOfSaleId", "The till's settings miss the bill type, money in or cash desk: press Paid at the till.");
                }

                var codes = link.Reference.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                var bills = (await pos.UnpaidAsync(billType, null, null, ct).ConfigureAwait(false)).Where(b => b.Code is { } c && codes.Contains(c)).ToList();
                if (bills.Count != codes.Count)
                {
                    throw ValidationException.For("billIds", "The bill is no longer open (paid at the till already?).");
                }

                if (decimal.Round(bills.Sum(b => b.Open), 2, MidpointRounding.AwayFromZero) != decimal.Round(link.Amount, 2))
                {
                    throw ValidationException.For("billIds", "The bill's amount changed after the payment link was made: press Paid at the till.");
                }

                var notice = await db.Set<BankNotice>().AsNoTracking().FirstAsync(n => n.Id == link.BankNoticeId, ct).ConfigureAwait(false);
                var line = new BankPaymentLine(notice.ExternalId, notice.BookedOn, notice.Amount, notice.Reference, notice.ProviderReference ?? notice.Reference, notice.PayerName,
                    notice.PayerIban, notice.Booked, true, notice.Kind);
                // Paid with the Gamma app: Gamma's open bill is marked paid, with the bank's text (Gamma finds its payment code in it).
                var open = link.GammaBillId is { } gammaBill ? new OpenGammaBill(gammaBill, link.GammaBillCode, link.GammaLink, notice.Reference, notice.ProviderReference) : null;
                var request = new PayByBankRequest(new PayBillsRequest(billType, moneyIn, cashDesk, [.. bills.Select(b => b.Id)], PaymentMethodId: link.PaymentMethodId,
                    PointOfSaleId: link.PointOfSaleId), line, open);
                var paid = await Endpoints.PayByBankAsync(pos, sp.GetRequiredService<PosBankPayments>(), sp.GetRequiredService<FiscalReceipts>(),
                    sp.GetRequiredService<GammaWalletBills>(), request, ct).ConfigureAwait(false);
                paymentId = paid[0].Id;
            }
            catch (Exception e) when (e is ValidationException or BankProviderException or InvalidOperationException or DbUpdateException)
            {
                error = e is ValidationException v ? string.Join(" ", v.Errors.Values) : e.Message;
                NotSettled(log, link.Id, e);
            }
        }

        // 3. What happened, on the link (its own row, nobody signed in).
        await using (var owner = scopes.CreateAsyncScope())
        {
            var db = owner.ServiceProvider.GetRequiredService<ErpDbContext>();
            var reason = error is { Length: > 500 } ? error[..500] : error;
            await db.Set<PaymentLink>().Where(l => l.Id == link.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.PaymentId, paymentId).SetProperty(l => l.SettleError, reason), ct)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment link {Link} was paid but could not pay its bill by itself.")]
    private static partial void NotSettled(ILogger logger, Guid link, Exception failure);
}

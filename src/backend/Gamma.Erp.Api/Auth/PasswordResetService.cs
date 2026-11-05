using System.Security.Cryptography;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Services.Mail;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using OpenIddict.Abstractions;

namespace Gamma.Erp.Api.Auth;

/// <summary>
/// The forgotten password: a short code sent to the user's e-mail address sets a new password. It works once, for a while, and only for a few tries, since a short code could
/// otherwise be guessed. No answer says whether a user exists or has an address.
/// </summary>
public sealed partial class PasswordResetService(ErpDbContext db, TenantContext tenant, TimeProvider clock, IMailSender sender, MailOptions mail,
    IOpenIddictTokenManager tokens, ILogger<PasswordResetService> log)
{
    /// <summary>How long a code works.</summary>
    public static readonly TimeSpan Valid = TimeSpan.FromMinutes(15);

    /// <summary>How long after a code no other is sent, so nobody can fill a user's mailbox.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMinutes(2);

    /// <summary>Wrong tries before a code stops working.</summary>
    public const int MaxTries = 5;

    /// <summary>The letters and digits of a code: none that can be taken for another (0 and O, 1 and I).</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int Length = 6;

    /// <summary>Whether this installation can send a code: without a mail server the sign-in page does not offer it.</summary>
    public bool Enabled => mail.Enabled;

    /// <summary>Sends a code to the user of this name, if there is one with an e-mail address.</summary>
    public async Task RequestAsync(string username, CancellationToken ct)
    {
        // Made before the user is looked up: how long the answer takes must not tell who exists.
        var code = RandomNumberGenerator.GetString(Alphabet, Length);
        var hash = SignInService.Hash(code);
        var name = SignInService.Normalize(username);
        if (!Enabled || await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Username == name, ct).ConfigureAwait(false) is not { Email.Length: > 0 } user)
        {
            return;
        }

        tenant.Set(user.TenantId, user.Id);
        var now = clock.GetUtcNow();
        var open = await db.Set<PasswordResetCode>().Where(c => c.UserId == user.Id && c.UsedOn == null).ToListAsync(ct).ConfigureAwait(false);
        if (open.Any(c => c.ExpiresOn - Valid > now - Gap))
        {
            return;
        }

        foreach (var replaced in open)
        {
            replaced.UsedOn = now;
        }

        db.Add(new PasswordResetCode { UserId = user.Id, CodeHash = hash, ExpiresOn = now + Valid, TenantId = user.TenantId, OwnerId = user.OwnerId });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Sent outside the request, for the same reason.
        var message = Message(user, code);
        _ = Task.Run(async () =>
        {
            try
            {
                await sender.SendAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Nobody is waiting: the failure goes to the log.
            catch (Exception failed)
#pragma warning restore CA1031
            {
                NotSent(log, failed);
            }
        }, CancellationToken.None);
    }

    /// <summary>Sets the new password and closes the code, ending the user's sessions; false when the code is wrong or no longer works.</summary>
    public async Task<bool> ResetAsync(string username, string code, string password, CancellationToken ct)
    {
        var name = SignInService.Normalize(username);
        var entered = (code ?? string.Empty).Trim().ToUpperInvariant();
        var now = clock.GetUtcNow();
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Username == name, ct).ConfigureAwait(false);
        var open = user is null ? null : await db.Set<PasswordResetCode>().Where(c => c.UserId == user.Id && c.UsedOn == null && c.ExpiresOn > now)
            .OrderByDescending(c => c.ExpiresOn).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (open is null)
        {
            SignInService.Matches(SignInService.Hash("timing"), entered); // the same work as for a real code
            return false;
        }

        tenant.Set(open.TenantId, open.UserId);
        if (!SignInService.Matches(open.CodeHash, entered))
        {
            open.Tries++;
            open.UsedOn = open.Tries >= MaxTries ? now : null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        await AccessService.StoreAsync(db, open.UserId, password, ct).ConfigureAwait(false);
        open.UsedOn = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tokens.RevokeBySubjectAsync(open.UserId.ToString(), ct).ConfigureAwait(false);
        return true;
    }

    private MimeMessage Message(User user, string code)
    {
        var message = new MimeMessage { Subject = Messages.Translate("Your Gamma ERP password code", user.Language) };
        message.From.Add(MailboxAddress.Parse(mail.From!));
        message.To.Add(MailboxAddress.Parse(user.Email!));
        message.Body = new TextPart("plain")
        {
            Text = $"""
                {Messages.Translate($"Someone asked for a new password for the Gamma ERP user {user.Username}.", user.Language)}

                {Messages.Translate($"Your code is {code}", user.Language)}

                {Messages.Translate($"Enter it on the sign-in page within {Valid.TotalMinutes:0} minutes. It works once.", user.Language)}

                {Messages.Translate("If it was not you, ignore this message: your password has not changed.", user.Language)}
                """,
        };
        return message;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The password code was not sent.")]
    private static partial void NotSent(ILogger logger, Exception failure);
}

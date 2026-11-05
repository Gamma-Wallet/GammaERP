using Gamma.Erp.Services.Printing;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Gamma.Erp.Services.Mail;

/// <summary>
/// The host's mail server (Mail:…): its address, how the connection is secured (StartTls by default, SslOnConnect, or None only for a
/// local test server), the account, and the sender every message goes out as. No host: e-mail is off.
/// </summary>
public sealed record MailOptions(string? Host, int Port, string Security, string? User, string? Password, string? From)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>Sends a message (a separate seam so a host can use another way out).</summary>
public interface IMailSender
{
    Task SendAsync(MimeMessage message, CancellationToken ct);
}

/// <summary>Over SMTP, with MailKit: a new connection per message, secured as the host says.</summary>
public sealed class SmtpMailSender(MailOptions options) : IMailSender
{
    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient();
        var security = Enum.TryParse<SecureSocketOptions>(options.Security, ignoreCase: true, out var s) ? s : SecureSocketOptions.StartTls;
        await client.ConnectAsync(options.Host ?? throw new InvalidOperationException("No Mail:Host."), options.Port, security, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(options.User))
        {
            await client.AuthenticateAsync(options.User, options.Password ?? "", ct).ConfigureAwait(false);
        }

        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
    }
}

/// <summary>What to send a document to: up to <see cref="MailService.MaxRecipients"/> addresses, an optional subject and message.</summary>
public sealed record SendDocumentRequest(IReadOnlyList<string> To, string? Subject = null, string? Message = null, string Language = "en");

/// <summary>A document sent: its printout attached, from the host's sender, to addresses checked one by one.</summary>
public sealed class MailService(PrintService print, IMailSender sender, MailOptions options)
{
    public const int MaxRecipients = 10;

    /// <summary>The message sent, or null when the request does not see the document.</summary>
    public async Task<string?> SendDocumentAsync(Guid documentId, SendDocumentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.Enabled)
        {
            throw ValidationException.For("mail", $"{ErrorCodes.MailError}: this installation has no mail server (Mail:Host and Mail:From).");
        }

        var to = new List<MailboxAddress>();
        foreach (var address in request.To ?? [])
        {
            if (Address(address) is not { } mailbox)
            {
                throw ValidationException.For("to", $"{ErrorCodes.MailError}: \"{address}\" is not an e-mail address.");
            }

            to.Add(mailbox);
        }

        if (to.Count is 0 or > MaxRecipients)
        {
            throw ValidationException.For("to", $"{ErrorCodes.MailError}: send to 1 to {MaxRecipients} addresses.");
        }

        if (await print.DocumentAsync(documentId, request.Language, ct).ConfigureAwait(false) is not { } pdf)
        {
            return null;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(options.From!));
        message.To.AddRange(to);
        message.Subject = Line(request.Subject) ?? Path.GetFileNameWithoutExtension(pdf.FileName);
        var body = new BodyBuilder { TextBody = request.Message ?? "" };
        body.Attachments.Add(pdf.FileName, pdf.Content, new ContentType("application", "pdf"));
        message.Body = body.ToMessageBody();
        await sender.SendAsync(message, ct).ConfigureAwait(false);
        return message.MessageId;
    }

    /// <summary>The mailbox of an address: an address only, never a display name or a list hidden in one string; null when it is not one.</summary>
    public static MailboxAddress? Address(string? address) =>
        MailboxAddress.TryParse(address?.Trim() ?? "", out var mailbox) && string.IsNullOrEmpty(mailbox.Name) && mailbox.Address.Contains('@', StringComparison.Ordinal)
            && !address!.Contains(',', StringComparison.Ordinal) && !address.Contains(';', StringComparison.Ordinal) ? mailbox : null;

    // A subject is one line (no header injection), at most 200 characters.
    private static string? Line(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : new string([.. text.Where(c => !char.IsControl(c))]).Trim() is { Length: > 0 } line ? line[..Math.Min(line.Length, 200)] : null;
}

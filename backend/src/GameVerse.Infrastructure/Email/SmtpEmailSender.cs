using GameVerse.Platform;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace GameVerse.Infrastructure.Email;

/// <summary>
/// SMTP settings, bound from <c>GameVerse:Email</c> (<c>GameVerse__Email__*</c>). Development points at Mailpit, which
/// catches every message; the owner's own mailbox (e.g. Gmail with an app password) or a transactional provider is set
/// through the same keys. See ADR-0006.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "GameVerse:Email";

    /// <summary>SMTP host. Empty = email off: messages are dropped with a warning (never fails a request).</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 1025;

    /// <summary>None (Mailpit), StartTls (port 587, e.g. Gmail) or SslOnConnect (port 465).</summary>
    public string Security { get; set; } = "None";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = "GameVerse <no-reply@gameverse.local>";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>Sends through SMTP with MailKit (MIT). One connection per message: fine for account emails' volume.</summary>
internal sealed class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();

        using var smtp = new SmtpClient();
        var security = Enum.Parse<SecureSocketOptions>(options.Security switch
        {
            "StartTls" => nameof(SecureSocketOptions.StartTls),
            "SslOnConnect" => nameof(SecureSocketOptions.SslOnConnect),
            _ => nameof(SecureSocketOptions.None),
        });
        await smtp.ConnectAsync(options.Host!, options.Port, security, ct).ConfigureAwait(false); // only registered when Host is set
        if (!string.IsNullOrEmpty(options.Username))
        {
            await smtp.AuthenticateAsync(options.Username, options.Password ?? string.Empty, ct).ConfigureAwait(false);
        }

        await smtp.SendAsync(mime, ct).ConfigureAwait(false);
        await smtp.DisconnectAsync(quit: true, ct).ConfigureAwait(false);
    }
}

/// <summary>Used when no SMTP host is configured: drops the message and says so. Never logs the body (it holds codes).</summary>
internal sealed partial class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> log) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogDropped(log, message.Subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "GV_EMAIL not configured (GameVerse__Email__Host empty); dropped \"{Subject}\"")]
    private static partial void LogDropped(ILogger logger, string subject);
}

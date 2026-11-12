namespace GameVerse.Platform;

/// <summary>An outgoing email: plain text plus a minimal HTML version. No tracking pixels or links to third parties.</summary>
public sealed record EmailMessage(string To, string Subject, string Text, string Html);

/// <summary>
/// Sends email. Implemented in Infrastructure (SMTP via MailKit); modules depend only on this interface, because
/// GameVerse.Platform cannot reference Infrastructure. Lives at the project root (architecture rule: only module folders).
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends the message. Throws when the mail server rejects it; callers decide whether that is fatal.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

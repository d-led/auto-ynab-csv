namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>
/// Gets the body of a message that lives on the server. Only the mail app can do that: a
/// <c>.partial.emlx</c> holds headers, and fetching the rest means asking Mail, which downloads it.
/// </summary>
public interface IMailBodySource
{
    /// <summary>The plain text of the message, or null when it cannot be had right now.</summary>
    string? BodyOf(MailRecord mail);
}

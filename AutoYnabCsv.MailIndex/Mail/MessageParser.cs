using MimeKit;

namespace AutoYnabCsv.MailIndex.Mail;

public static class MessageParser
{
    /// <summary>
    /// The shape of the text this parser yields. Bump it whenever that shape changes, so an index
    /// built by an older reading is read again rather than trusted.
    /// </summary>
    public const string TextVersion = "lines-1";

    public static MailRecord Parse(EmlxMessage emlx)
    {
        using var stream = new MemoryStream(emlx.RawMessage, writable: false);
        var message = MimeMessage.Load(stream);
        var sender = message.From.Mailboxes.FirstOrDefault();

        return new MailRecord(
            emlx.Path,
            emlx.ReceivedAt ?? message.Date,
            message.Subject ?? string.Empty,
            sender?.Address ?? string.Empty,
            sender?.Name ?? string.Empty,
            PlainTextOf(message),
            message.MessageId ?? string.Empty
        );
    }

    private static string PlainTextOf(MimeMessage message)
    {
        var plain = message.TextBody;
        return string.IsNullOrWhiteSpace(plain)
            ? HtmlToText.Convert(message.HtmlBody ?? string.Empty)
            : plain;
    }
}

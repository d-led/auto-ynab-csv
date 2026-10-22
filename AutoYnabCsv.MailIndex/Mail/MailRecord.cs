namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>A message reduced to what correlation needs, with the body as plain text.</summary>
public sealed record MailRecord(
    string Path,
    DateTimeOffset ReceivedAt,
    string Subject,
    string SenderAddress,
    string SenderName,
    string Text,
    string MessageId = ""
);

using System.Text.RegularExpressions;
using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Correlation;

/// <summary>
/// The numbers a bank row quotes that a mail would quote back: an order number, a payment
/// processor's transaction code, a merchant's order or invoice number.
/// </summary>
public static partial class References
{
    /// <summary>How many numbers a row may offer; a longer list is a bank's own noise.</summary>
    private const int MaxReferences = 3;

    /// <summary>The numbers in what the row says about itself.</summary>
    public static IReadOnlyList<string> Of(CorrelationEntry entry) =>
        Pattern()
            .Matches($"{entry.Payee} {entry.Memo}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxReferences)
            .ToArray();

    /// <summary>The first reference the row and the mail both quote; null when they share none.</summary>
    public static string? Shared(CorrelationEntry entry, MailFacts mail) =>
        Of(entry).FirstOrDefault(reference => Quotes(mail, reference));

    /// <summary>The numbers a mail quotes in what was extracted from it, for the lookup by number.</summary>
    public static IReadOnlyList<string> In(MailFacts mail) =>
        Pattern()
            .Matches(Quoted(mail))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>What a mail says about itself, before its body is in play.</summary>
    private static string Quoted(MailFacts mail) =>
        string.Join(
            '\n',
            mail.Mail.Subject,
            string.Join('\n', mail.OrderNumbers),
            string.Join('\n', mail.Fields.Values),
            string.Join('\n', mail.Items)
        );

    /// <summary>Where a mail might quote a number: its fields, its subject, its body, its items.</summary>
    private static bool Quotes(MailFacts mail, string reference) =>
        mail.OrderNumbers.Contains(reference, StringComparer.Ordinal)
        || mail.Mail.Subject.Contains(reference, StringComparison.Ordinal)
        || mail.Mail.Text.Contains(reference, StringComparison.Ordinal)
        || mail.Fields.Values.Any(value => value.Contains(reference, StringComparison.Ordinal))
        || mail.Items.Any(item => item.Contains(reference, StringComparison.Ordinal));

    [GeneratedRegex(@"\b\d{3}-\d{7}-\d{7}\b|\b[A-Z0-9]{17}\b|\b\d{9,}\b", RegexOptions.Compiled)]
    private static partial Regex Pattern();
}

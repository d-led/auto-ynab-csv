using AutoYnabCsv.MailIndex.Mail;

namespace AutoYnabCsv.MailIndex.Facts;

/// <summary>
/// What one message contributes to correlation: who, which order, how much, and any fields the
/// configured extraction rules found.
/// </summary>
public sealed record MailFacts(
    MailRecord Mail,
    string Vendor,
    IReadOnlyList<string> OrderNumbers,
    IReadOnlyList<decimal> Amounts,
    IReadOnlyDictionary<string, string> Fields
)
{
    /// <summary>
    /// The items the rules read out of the message, one per entry. Empty when the mail says
    /// nothing about what was bought.
    /// </summary>
    public IReadOnlyList<string> Items =>
        Fields.TryGetValue(Extraction.FieldExtraction.ItemsName, out var lines)
            ? lines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}

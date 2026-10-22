using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace AutoYnabCsv.MailIndex.Extraction;

/// <summary>
/// One named field, extracted with a pattern; the first group wins, else the whole match.
/// The field named <see cref="ItemsName"/> is the one exception: it keeps every match,
/// because a purchase has more than one item.
/// </summary>
/// <param name="Where">
/// Which part of the message the field is read from. A single value is quoted anywhere, but a
/// list of goods is read from one place only: the body says what was bought a line at a time,
/// and the subject says it in the shop's own shortened words — never both, or the same purchase
/// would be named twice.
/// </param>
public sealed record FieldExtraction(string Name, string Pattern, FieldSource Where = FieldSource.Anywhere)
{
    /// <summary>Holds what was bought, one match per line.</summary>
    public const string ItemsName = "items";
}

/// <summary>Which part of a message a field may be read from.</summary>
public enum FieldSource
{
    /// <summary>Subject and body alike — how a reference number is quoted.</summary>
    Anywhere,

    /// <summary>The body alone — how a line item is written.</summary>
    Body,

    /// <summary>The subject alone — how a shop summarizes the purchase in one line.</summary>
    Subject,
}

/// <summary>
/// A named extraction rule: applies when every stated condition matches somewhere in the
/// message, then fills the fields it declares. <paramref name="Stop"/> ends evaluation.
/// </summary>
public sealed record ExtractionRule(
    string Id,
    string? Sender = null,
    string? Subject = null,
    string? Body = null,
    string? Vendor = null,
    IReadOnlyList<FieldExtraction>? Fields = null,
    bool Stop = false
);

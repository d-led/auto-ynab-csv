using System.Text.RegularExpressions;

namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>
/// Mail bodies carry invisible direction marks and non-breaking spaces: Amazon writes an order
/// number as <c>Order # ‫028-1234567-8901234</c>, where the mark sits between the hash and the
/// digits and splits the number for every pattern that looks at it. That is formatting, not
/// content, so it is removed before anything is matched.
/// </summary>
public static class PlainText
{
    public static string Normalize(string text) => Invisible.Replace(Spaces(text), string.Empty);

    /// <summary>A non-breaking space is a space: shops use it to keep a price on one line.</summary>
    private static string Spaces(string text) =>
        text.Replace('\u00A0', ' ').Replace('\u202F', ' ').Replace('\u2007', ' ');

    private static readonly Regex Invisible = new(
        @"[\u200B-\u200F\u202A-\u202E\u2066-\u2069\uFEFF]",
        RegexOptions.Compiled
    );
}

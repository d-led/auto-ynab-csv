using System.Text.RegularExpressions;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Mail;

namespace AutoYnabCsv.MailIndex.Facts;

/// <summary>
/// The built-in signals a mail carries, plus the named fields the extraction rules add.
/// </summary>
public static class SignalExtractor
{
    private const string Number = "[0-9]{1,3}(?:[.,][0-9]{3})*[.,][0-9]{2}";

    private static readonly Regex OrderNumber = new(
        @"\b\d{3}-\d{7}-\d{7}\b",
        RegexOptions.Compiled
    );

    private static readonly Regex Amount = new(
        $@"(?:(?:EUR|€)\s*(?<amount>{Number})|(?<amount>{Number})\s*(?:EUR|€))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static MailFacts Extract(MailRecord mail, ExtractionRuleSet? rules = null)
    {
        var readable = Readable(mail);
        var text = string.Join('\n', readable.Subject, readable.Text);
        var fields = rules?.Extract(readable) ?? new Dictionary<string, string>();

        return new MailFacts(
            readable,
            VendorOf(readable, rules),
            OrderNumbersOf(text, fields),
            AmountsOf(text),
            fields
        );
    }

    /// <summary>Invisible marks are dropped once, here, so no pattern has to know about them.</summary>
    private static MailRecord Readable(MailRecord mail) =>
        mail with
        {
            Subject = PlainText.Normalize(mail.Subject),
            Text = PlainText.Normalize(mail.Text),
        };

    private static string VendorOf(MailRecord mail, ExtractionRuleSet? rules) =>
        rules?.VendorOf(mail) is { Length: > 0 } fromRules
            ? fromRules
            : (rules?.Vendors ?? RuleSetLoader.ShippedVendors).Of(mail.SenderAddress);

    private static IReadOnlyList<string> OrderNumbersOf(
        string text,
        IReadOnlyDictionary<string, string> fields
    ) =>
        OrderNumber
            .Matches(text)
            .Select(match => match.Value)
            .Concat(fields.TryGetValue("orderNumber", out var order) ? [order] : [])
            .Distinct()
            .ToArray();

    private static IReadOnlyList<decimal> AmountsOf(string text) =>
        Amount
            .Matches(text)
            .Select(match => match.Groups["amount"].Value)
            .Select(value => AmountParser.TryParse(value, out var amount) ? amount : (decimal?)null)
            .Where(amount => amount is not null)
            .Select(amount => amount!.Value)
            .Distinct()
            .ToArray();
}

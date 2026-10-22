using System.Text.RegularExpressions;

namespace AutoYnabCsv.MailIndex.Enrichment;

/// <summary>What a bank entry itself reveals once its payee and memo are read carefully.</summary>
public sealed record EntryFacts(
    string Payee,
    string Merchant,
    string OrderNumber,
    bool LooksLikeTransfer
);

/// <summary>
/// Bank exports carry more than they seem: PayPal writes the merchant into the memo
/// ("…, Ihr Einkauf bei Thomann GmbH"), Amazon writes the order number, and N26 shortens the
/// merchant to "PAYPAL *AUDIOPARTNE". These patterns read that without touching mail.
/// </summary>
public static class EntryPatterns
{
    private static readonly Regex PaypalMerchant = new(
        @"Ihr Einkauf bei\s+(?<merchant>[^,]+?)(?:\s*$|/ABBUCHUNG)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex AmazonOrder = new(
        @"\b\d{3}-\d{7}-\d{7}\b",
        RegexOptions.Compiled
    );

    private static readonly Regex PaypalStar = new(
        @"^PAYPAL\s*\*\s*(?<token>[A-Z0-9][A-Z0-9 ]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex Collapse = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>Generic banking words for a movement between your own accounts; no names, no IBANs.</summary>
    private static readonly Regex TransferWords = new(
        @"(Umbuchung|Übertrag|Eigenübertrag|Transfer|Ausgleich|Kreditkartenabrechnung|Tagesgeld|Sparbuch|Depot)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// A card statement being paid off: <c>KREDITKARTENABRECHNUNG VISA -ABR.</c> on the current
    /// account, <c>Ausgleich Kreditkarte gem</c> on the card account.
    /// </summary>
    private static readonly Regex CardSettlement = new(
        @"(Kreditkarten?abrechnung|Ausgleich\s+Kreditkarte)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static EntryFacts Of(string payee, string memo)
    {
        var normalizedPayee = Collapse.Replace(payee, " ").Trim();

        return new EntryFacts(
            normalizedPayee,
            MerchantOf(normalizedPayee, memo),
            AmazonOrder.Match(memo).Value,
            TransferWords.IsMatch(normalizedPayee) || TransferWords.IsMatch(memo)
        );
    }

    /// <summary>
    /// True when the row says it settles a card, rather than that something was bought. Read from
    /// the row alone, so the pairing can allow for the days a settlement needs.
    /// </summary>
    public static bool IsCardSettlement(string payee, string memo) =>
        CardSettlement.IsMatch(payee) || CardSettlement.IsMatch(memo);

    private static string MerchantOf(string payee, string memo) =>
        PaypalMerchantOf(memo)
        ?? AmazonOf(payee)
        ?? PaypalStarOf(payee)
        ?? payee;

    private static string? PaypalMerchantOf(string memo) =>
        PaypalMerchant.Match(memo) is { Success: true } match
            ? match.Groups["merchant"].Value.Trim()
            : null;

    private static string? AmazonOf(string payee) =>
        payee.Contains("AMAZON", StringComparison.OrdinalIgnoreCase) ? "Amazon" : null;

    private static string? PaypalStarOf(string payee) =>
        PaypalStar.Match(payee) is { Success: true } match
            ? $"PayPal *{match.Groups["token"].Value.Trim()}"
            : null;
}

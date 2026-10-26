using System.Text.RegularExpressions;

namespace AutoYnabCsv.MailIndex.Enrichment;

/// <summary>
/// The lines shops print around a purchase that say nothing about it: a return policy, a "view your
/// order" prompt, the sentence naming the legal entity at the foot of every mail. A memo is read in a
/// bank app on a phone — those words only push the thing that was bought out of sight.
///
/// Whole phrases, matched without regard to case, so a title that merely contains one of the words
/// survives: "Beispiel Ersatzteil für Ihre Bestellung" is a thing that was bought, and stays.
/// </summary>
public static class MemoNoise
{
    private static readonly string[] Phrases =
    [
        "Return or replace items in Your Orders",
        "View or edit order",
        "View order details",
        "Your Orders",
        "Your Account",
        "Amazon.de is a trading name for Amazon EU Sarl",
        "Die Widerrufsbelehrung finden Sie im letzten Abschnitt",
        "Sie können die Details",
        "Details Ihres Einkaufs",
        "Hier sind die Details zu Ihrem Einkauf",
        "Privacy Notice",
        "Transaktionsdetails",
    ];

    private static readonly Regex Any = new(
        string.Join("|", Phrases.Select(Regex.Escape)),
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>What is left once the phrases are out; spaces, so no two words are fused.</summary>
    public static string Without(string text) => Any.Replace(text, " ");
}

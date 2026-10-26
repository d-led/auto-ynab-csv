using System.Text.RegularExpressions;

namespace AutoYnabCsv.MailIndex.Enrichment;

/// <summary>
/// The lines shops print around a purchase that say nothing about it: a return policy, a "view your
/// order" prompt, the sentence naming the legal entity at the foot of every mail. A memo is read in a
/// bank app on a phone — those words only push the thing that was bought out of sight.
///
/// Harvested from real conversions rather than guessed: every pattern below was found repeating in
/// the memos of a mailbox of shop mail. Order matters — the longer, more specific text comes first,
/// so a sentence is removed as a sentence instead of leaving its tail behind, and a title that merely
/// contains one of the words survives ("Beispiel Ersatzteil für Ihre Bestellung" was bought).
///
/// An e-mail address goes first of all: it is somebody's data, not a purchase.
/// </summary>
public static class MemoNoise
{
    private static readonly string[] Patterns =
    [
        // Somebody's address. Never worth a memo, and not ours to copy around.
        @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",

        // The lines Amazon puts under every order, in both languages. Counted in the
        // conversions they came from: 108, 168, 80, 42, 40, 22, 12.
        @"You can view the status of your order[^·]*",
        @"Need to make changes to your order\?[^·]*",
        @"Return or replace items in Your Orders:?",
        @"Um Artikel zurückzusenden oder zu ersetzen[^·]*",
        @"Artikel in Meine Bestellungen zurückgeben oder ersetzen:?",
        @"We hope to see you again soon\.?",
        @"Amazon\.de is a trading name for Amazon EU Sarl",
        @"Amazon\.de Order Confirmation",
        @"Amazon\.de Bestellbestätigung",
        @"View or edit order",
        @"View order details",
        @"Your Orders",
        @"Your Account",

        // What a product card says about itself rather than about the product.
        @"Condition:\s*New",
        @"Shipment \d+ of \d+",
        @"Fulfilled by Amazon",
        @"Arriving:?",
        @"Gesamtbetrag der Bestellung:?",

        // The receipts' own prose.
        @"Hier sind die Details zu Ihrem Einkauf[^·]*",
        @"Details Ihres Einkaufs",
        @"Die Widerrufsbelehrung finden Sie[^·]*",
        @"Sie können die Details[^·]*",
        @"Privacy Notice",
        @"Transaktionsdetails",
        @"Onlinezahlung",
        @"Urlaub Hilfe",
    ];

    private static readonly Regex Any = new(
        string.Join("|", Patterns),
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>What is left once the phrases are out; spaces, so no two words are fused.</summary>
    public static string Without(string text) => Any.Replace(text, " ");
}

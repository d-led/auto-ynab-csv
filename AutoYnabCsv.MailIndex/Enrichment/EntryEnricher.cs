using System.Globalization;
using System.Text.RegularExpressions;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Enrichment;

/// <summary>One transaction as it should be imported: the bank's row, plus what the mail adds.</summary>
public sealed record EnrichedEntry(CorrelationEntry Entry, string Payee, string Memo);

/// <summary>
/// Turns a bank entry into the row YNAB sees. YNAB knows nothing about the mail that explains a
/// purchase, so the memo carries the gist of it — the items with their prices, the order number,
/// and where the purchase really happened (<c>via paypal</c>). Without a mail the row stays as the
/// bank wrote it: nothing is invented.
/// </summary>
public static class EntryEnricher
{
    /// <summary>
    /// One line in YNAB; longer text is cut, not silently truncated mid-word. Long enough for an
    /// order number and the whole title of every item of a parcel, which is what a row is for.
    /// </summary>
    public const int MaxMemoLength = 500;

    private const int MaxItems = 3;
    private const string Separator = " · ";
    private const string Cut = "…";

    public static EnrichedEntry Of(CorrelationEntry entry, MailFacts? mail) =>
        Of(entry, mail is null ? [] : [mail]);

    /// <summary>
    /// Several mails can explain one purchase: the confirmation lists the goods, the delivery note
    /// says they arrived. Each is read for what it knows, and the parts add up.
    /// </summary>
    public static EnrichedEntry Of(CorrelationEntry entry, IReadOnlyList<MailFacts> mails)
    {
        var payee = PayeeOf(entry, EntryPatterns.Of(entry.Payee, entry.Memo), mails);

        return new EnrichedEntry(entry, payee, MemoOf(entry, payee, mails));
    }

    /// <summary>
    /// Who the money really went to. The receipt knows: it names the shop it was paid to, where the
    /// bank only names whoever collected the money (<c>Ihr Einkauf bei PayPal Europe</c> says
    /// nothing about the shop). What the row reveals counts next, then the vendor, then the bank's
    /// own words — and a processor is never an answer.
    /// </summary>
    private static string PayeeOf(
        CorrelationEntry entry,
        EntryFacts facts,
        IReadOnlyList<MailFacts> mails
    ) =>
        new[] { MerchantIn(mails), facts.Merchant, VendorIn(mails) }
            .FirstOrDefault(NamesSomeone)
        ?? entry.Payee;

    /// <summary>
    /// Who the mail says was on the other side: the shop a purchase went to, or the person who was
    /// paid — PayPal names the second kind only as "an <c>nicky beispiel</c> gezahlt".
    /// </summary>
    private static string? MerchantIn(IReadOnlyList<MailFacts> mails) =>
        mails.Select(mail => mail.Fields.GetValueOrDefault("merchant"))
            .Concat(mails.Select(mail => mail.Fields.GetValueOrDefault("recipient")))
            .FirstOrDefault(NamesSomeone);

    private static string VendorIn(IReadOnlyList<MailFacts> mails) =>
        mails.Select(mail => mail.Vendor).FirstOrDefault(vendor => vendor.Length > 0)
        ?? string.Empty;

    private static bool NamesSomeone(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && !IsProcessor(candidate);

    /// <summary>
    /// Collecting the money is not selling anything: "PayPal Europe S.a.r.l." as a payee leaves the
    /// row no clearer than the bank did.
    /// </summary>
    private static bool IsProcessor(string name) =>
        name.Contains("paypal", StringComparison.OrdinalIgnoreCase);

    private static string MemoOf(
        CorrelationEntry entry,
        string payee,
        IReadOnlyList<MailFacts> mails
    )
    {
        var parts = new List<string>();
        var numbers = OrderNumbersIn(mails);
        var codes = CodesIn(mails);
        var bank = BankOf(WithoutLinks(entry.Memo), numbers, codes);

        // The references come first: the order number the shop and the bank agree on, then the
        // code PayPal answers a question with. Either is what a person searches a register by.
        // What was bought follows, then the bank's own words.
        parts.Add(OrderPart(numbers));
        parts.Add(CodePart(codes));
        parts.AddRange(ItemsOf(mails));
        parts.Add(Remainder(payee, bank));
        parts.AddRange(SourceOf(payee, entry.Memo, mails));

        var memo = string.Join(
            Separator,
            parts.Where(part => part.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
        );

        return Shorten(Tidy(WithoutAmount(memo, entry.Amount)));
    }

    /// <summary>
    /// The bank's own words, unless the payee already says them: "PAYPAL" beside "PayPal Europe
    /// S.a.r.l." is the same word twice, and the memo is no place for an echo.
    /// </summary>
    private static string Remainder(string payee, string bank) =>
        bank.Length > 0 && payee.Contains(bank, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : bank;

    private static IEnumerable<string> ItemsOf(IReadOnlyList<MailFacts> mails)
    {
        var bought = mails.SelectMany(mail => mail.Items)
            .Select(BoughtName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var listed = WholeTitles(bought).Take(MaxItems).ToArray();

        if (listed.Length > 0)
        {
            yield return string.Join(Separator, listed);
        }

        if (bought.Length > listed.Length)
        {
            yield return $"+{bought.Length - listed.Length} more";
        }
    }

    /// <summary>
    /// A shop shortens a title where it has no room — "Hoffenbach Magnetic Child…" in a subject —
    /// and prints it whole further down the mail. A name cut short is only worth a memo when
    /// nothing whole was found, so it is dropped rather than listed beside the full one.
    /// </summary>
    private static IReadOnlyList<string> WholeTitles(IReadOnlyList<string> names) =>
        names.Any(name => !CutShort(name)) ? names.Where(name => !CutShort(name)).ToArray() : names;

    private static bool CutShort(string name) =>
        name.EndsWith('…') || name.EndsWith("...", StringComparison.Ordinal);

    /// <summary>
    /// A memo is read on a phone in a bank app: a URL is noise there, and a link is not something
    /// you bought. Whatever a rule or the bank wrote, no address reaches the row.
    /// </summary>
    private static string WithoutLinks(string text) =>
        Squash(Links.Replace(text, string.Empty));

    /// <summary>
    /// The row already carries its amount in its own column, so a memo that says it again is noise
    /// — the receipt's total, an item's price when it is the whole purchase, the bank's own
    /// "Betrag 84,00 €". A price label in front of it goes too; every other price stays.
    /// </summary>
    private static string WithoutAmount(string memo, decimal amount) =>
        Price.Replace(
            memo,
            match => AmountIn(match.Groups["money"].Value) == amount ? string.Empty : match.Value
        );

    private static decimal? AmountIn(string text) =>
        decimal.TryParse(
            text.Replace(".", string.Empty).Replace(',', '.'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var thousands
        )
            ? thousands
            : null;

    /// <summary>What removing a part leaves: no separator with nothing between, no double space.</summary>
    private static string Tidy(string memo) =>
        string.Join(
            Separator,
            memo.Split(Separator)
                .Select(part => Squash(part.Trim(' ', ',', ';', '·')))
                .Where(part => part.Length > 0)
        );

    /// <summary>What is left of a line once an address is taken out of it, if anything is.</summary>
    private static string? BoughtName(string item) =>
        WithoutLinks(item) is { Length: >= 3 } name ? name : null;

    private static string Squash(string text) =>
        Spaces.Replace(text.Trim(' ', ',', ';', '·'), " ");

    private static IReadOnlyList<string> OrderNumbersIn(IReadOnlyList<MailFacts> mails) =>
        mails.SelectMany(mail => mail.OrderNumbers).Distinct(StringComparer.Ordinal).ToArray();

    private static string OrderPart(IReadOnlyList<string> numbers) =>
        numbers.Count == 0 ? string.Empty : $"Order {string.Join(", ", numbers)}";

    /// <summary>
    /// The code PayPal gave the payment. The number the bank prints beside it —
    /// <c>1000000000001</c> — finds a mailbox full of PayPal mail, and nothing precise; the code
    /// finds the one payment.
    /// </summary>
    private static IReadOnlyList<string> CodesIn(IReadOnlyList<MailFacts> mails) =>
        mails.Select(mail => mail.Fields.GetValueOrDefault("transactionId")?.Trim())
            .OfType<string>()
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string CodePart(IReadOnlyList<string> codes) =>
        codes.Count == 0 ? string.Empty : $"Transaktion {string.Join(", ", codes)}";

    /// <summary>
    /// The bank's own words with nothing the memo already says: a number that is printed first is
    /// not printed twice, and the marketplace code the row ends with — "AMZN Mktp DE
    /// 6OQGM6ZEBNOJ151F" — names neither the goods nor the shop.
    /// </summary>
    private static string BankOf(
        string memo,
        IReadOnlyList<string> numbers,
        IReadOnlyList<string> codes
    )
    {
        // With a code to print, PayPal's own reference has told its story — and that code is
        // printed once, whether the bank had it or only the mail did.
        var rest = codes.Count > 0 ? PaypalReference.Replace(memo, string.Empty) : memo;

        foreach (var reference in numbers.Concat(codes))
        {
            rest = rest.Replace(reference, string.Empty, StringComparison.Ordinal);
        }

        return Tidy(Dangling(MarketCode.Replace(rest, string.Empty)));
    }

    /// <summary>"Ihr Einkauf bei" with nothing after it says the bank's sentence was cut short.</summary>
    private static string Dangling(string text) =>
        DanglingLabel.Replace(text, string.Empty);

    /// <summary>
    /// PayPal's own reference, <c>1000000000001/PP.8625.PP/. </c>, which the code from the receipt
    /// replaces: it names the payment to PayPal and nothing to a person.
    /// </summary>
    private static readonly Regex PaypalReference = new(
        @"^\s*[0-9]{10,}\s*/?\s*PP\.[0-9A-Z.]*\s*/?\s*[,.]?\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex DanglingLabel = new(
        @"^\s*,?\s*Ihr Einkauf bei\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// Names the service that stood between the account and the merchant, but only when the row
    /// does not already say it: "via paypal" next to "PayPal Europe S.a.r.l." is noise. Once the
    /// payee is the real shop, it is the one clue YNAB cannot derive on its own.
    /// </summary>
    private static IEnumerable<string> SourceOf(
        string payee,
        string memo,
        IReadOnlyList<MailFacts> mails
    )
    {
        var vendor = ServiceBehind(mails);

        return vendor.Length == 0 || Mentions(payee, memo, vendor)
            ? []
            : [$"via {vendor.ToLowerInvariant()}"];
    }

    /// <summary>
    /// The mail that named the shop is the one that came from the service: a purchase that PayPal
    /// carried is "via paypal" even when the shop's own confirmation was read too, and the shop's
    /// confirmation says nothing about who carried the money.
    /// </summary>
    private static string ServiceBehind(IReadOnlyList<MailFacts> mails)
    {
        var namer = mails.FirstOrDefault(NamesTheOtherSide);

        return namer is null || namer.Vendor.Length == 0 ? VendorIn(mails) : namer.Vendor;
    }

    private static bool NamesTheOtherSide(MailFacts mail) =>
        NamesSomeone(mail.Fields.GetValueOrDefault("merchant"))
        || NamesSomeone(mail.Fields.GetValueOrDefault("recipient"));

    private static bool Mentions(string payee, string memo, string word) =>
        payee.Contains(word, StringComparison.OrdinalIgnoreCase)
        || memo.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An amount as a bank or a shop prints it — "84,00", "EUR 84.00", "Betrag 84,00 €" — with the
    /// label that announces it, so what is left of the sentence is still a sentence.
    /// </summary>
    private static readonly Regex Price = new(
        @"(?:\b(?:Betrag|Gesamtbetrag|Gesamtsumme|Summe|Order\s+Total|Total)\b\s*:?\s*)?"
            + @"(?:(?:EUR|€)\s*)?(?<money>[0-9]{1,3}(?:[.,][0-9]{3})*[.,][0-9]{2})\s*(?:EUR|€)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// The code a bank appends to a marketplace row: capital letters and digits, twelve or more, at
    /// the end and of no use once the goods and the shop are named.
    /// </summary>
    private static readonly Regex MarketCode = new(
        @"\s+(?=[A-Z0-9]*[0-9])[A-Z0-9]{12,}\s*$",
        RegexOptions.Compiled
    );

    /// <summary>An address, with or without a scheme; the word before it stays.</summary>
    private static readonly Regex Links = new(
        @"(?:https?://|www\.)\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.Compiled);

    private static string Shorten(string memo) =>
        memo.Length <= MaxMemoLength
            ? memo
            : memo[..(MaxMemoLength - Cut.Length)].TrimEnd() + Cut;
}

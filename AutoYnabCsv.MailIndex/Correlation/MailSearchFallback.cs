using System.Text.RegularExpressions;
using AutoYnabCsv.MailIndex.Enrichment;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace AutoYnabCsv.MailIndex.Correlation;

/// <summary>
/// What a search hit can be read from: the file on disk, which may hold headers only, and the
/// step that fills in a body the mail app has to download. <see cref="WithBody"/> must be a
/// no-op for a message that is already complete, and must not fetch the same message twice.
/// </summary>
public sealed record MailLookup(Func<string, MailFacts?> FromFile, Func<MailFacts, MailFacts?> WithBody)
{
    /// <summary>Only what is on disk; nothing is downloaded.</summary>
    public static MailLookup Local(Func<string, MailFacts?> fromFile) => new(fromFile, facts => facts);
}

/// <summary>What the search did, so a run can say it instead of leaving the user guessing.</summary>
public sealed record MailSearchResult(
    IReadOnlyList<Match> Matches,
    int Entries,
    int Candidates,
    int Fetched,
    int NotWorthSearching = 0
)
{
    public static MailSearchResult None { get; } = new([], 0, 0, 0);

    /// <summary>
    /// What the index held, with the bodies this search read put in their place: the same message,
    /// now saying what was bought, rather than the header-only copy beside it.
    /// </summary>
    public IReadOnlyList<MailFacts> Replacing(IReadOnlyList<MailFacts> indexed)
    {
        var byPath = indexed.ToDictionary(facts => facts.Mail.Path, StringComparer.Ordinal);

        foreach (var match in Matches)
        {
            byPath[match.Mail.Mail.Path] = match.Mail;
        }

        return byPath.Values.ToArray();
    }
}

/// <summary>
/// The second pass for entries the index could not explain: search the mailbox by the merchant
/// the entry names, read what comes back, and keep only what fits the amount and the day. It is
/// the same judgement the manual search made — look it up, then check it holds.
/// </summary>
public static class MailSearchFallback
{
    public static MailSearchResult Search(
        IReadOnlyList<CorrelationEntry> entries,
        IReadOnlyList<Match> found,
        IMailFinder finder,
        MailLookup lookup,
        Func<CorrelationEntry, MailQuery> query,
        Action<EntrySearch>? progress = null
    )
    {
        var searched = new Dictionary<string, IReadOnlyList<MailFacts>>(
            StringComparer.OrdinalIgnoreCase
        );
        var extra = new List<Match>();
        var fetched = new HashSet<string>(StringComparer.Ordinal);
        var unexplained = Unexplained(entries, found).ToArray();
        var candidates = 0;

        foreach (var entry in unexplained)
        {
            var asked = query(entry);
            progress?.Invoke(new EntrySearch(entry.Date, entry.Payee, entry.Amount, asked));

            foreach (var candidate in Closest(entry, Candidates(asked, finder, lookup.FromFile, searched)))
            {
                candidates++;

                if (Match(entry, asked, candidate, lookup, fetched) is not { } match)
                {
                    continue;
                }

                extra.Add(match);
                extra.AddRange(ShopBehind(entry, asked, match, finder, lookup, searched, fetched));

                // One message answers a row. An order number matches the confirmation, the
                // dispatch note and the delivery note, and asking Mail for all of them is how a
                // mailbox gets hammered; the first that fits is the one the row is built from.
                break;
            }
        }

        return new MailSearchResult(
            extra.ByEvidence().ToArray(),
            unexplained.Length,
            candidates,
            fetched.Count,
            entries.Count - unexplained.Length
        );
    }

    /// <summary>
    /// The shop behind the processor. A PayPal receipt names the shop the money went to but rarely
    /// says what was bought; the shop's own mail does, and the row names no merchant at all — the
    /// receipt's word is what makes it the same purchase. One more lookup, and only when the first
    /// one had no shop to search for: a row that says <c>PAYPAL *THOMANN</c> already looked.
    /// </summary>
    private static IEnumerable<Match> ShopBehind(
        CorrelationEntry entry,
        MailQuery asked,
        Match receipt,
        IMailFinder finder,
        MailLookup lookup,
        IDictionary<string, IReadOnlyList<MailFacts>> searched,
        ISet<string> fetched
    )
    {
        if (ShopNamedBy(entry, receipt.Mail) is not { } shop || AlreadySearched(asked, shop))
        {
            yield break;
        }

        var hop = new MailQuery([], ShopName.Head(shop), entry.Date, Correlator.AmountWindowDays);

        foreach (var candidate in Closest(entry, Candidates(hop, finder, lookup.FromFile, searched)))
        {
            if (
                candidate.Mail.Path == receipt.Mail.Mail.Path
                || !candidate.Amounts.Contains(entry.Amount)
                || Correlator.DayDistance(entry, candidate) > Correlator.AmountWindowDays
            )
            {
                continue;
            }

            if (Complete(candidate, lookup, fetched) is not { } mail || !Names(mail, shop))
            {
                continue;
            }

            yield return new Match(
                entry,
                mail,
                Correlator.DayDistance(entry, mail),
                new Evidence(
                    EvidenceKind.AmountAndCounterparty,
                    $"{Correlator.AmountText(entry.Amount)} · {shop} named by {receipt.Mail.Vendor}"
                )
            );

            yield break;
        }
    }

    /// <summary>
    /// True when the first lookup already asked for this shop — the row itself named it, as
    /// <c>PAYPAL *THOMANN</c> names Thomann — so a second search would only repeat the first.
    /// </summary>
    private static bool AlreadySearched(MailQuery asked, string shop) =>
        asked.Phrase.Length > 0
        && (
            asked.Phrase.Contains(ShopName.Head(shop), StringComparison.OrdinalIgnoreCase)
            || shop.Contains(asked.Phrase, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>The merchant the receipt states, when it is a name the row itself never says.</summary>
    private static string? ShopNamedBy(CorrelationEntry entry, MailFacts receipt) =>
        receipt.Fields.GetValueOrDefault("merchant") is { Length: > 0 } shop
        && !entry.Says(ShopName.Head(shop))
            ? shop
            : null;

    /// <summary>The mail says the shop's name, in its own words or as its sender's.</summary>
    private static bool Names(MailFacts mail, string shop) =>
        mail.Fields.GetValueOrDefault("merchant")
            ?.Contains(ShopName.Head(shop), StringComparison.OrdinalIgnoreCase)
            is true
        || mail.Vendor.Contains(ShopName.Head(shop), StringComparison.OrdinalIgnoreCase);

    /// <summary>The candidates in the order worth reading: the day decides before a body is asked for.</summary>
    private static IEnumerable<MailFacts> Closest(
        CorrelationEntry entry,
        IReadOnlyList<MailFacts> candidates
    ) =>
        candidates
            .OrderBy(candidate => Correlator.DayDistance(entry, candidate))
            .ThenBy(candidate => candidate.Mail.Path, StringComparer.Ordinal);

    /// <summary>
    /// Asking Mail for a body is the expensive part, so the date decides before anything is
    /// downloaded. How wide that is depends on what was asked for: a number the row quotes buys the
    /// whole search window, because the mail that answers it can arrive when the goods do.
    /// </summary>
    private static Match? Match(
        CorrelationEntry entry,
        MailQuery asked,
        MailFacts candidate,
        MailLookup lookup,
        ISet<string> fetched
    )
    {
        if (Correlator.DayDistance(entry, candidate) > asked.WindowDays)
        {
            return null;
        }

        return Complete(candidate, lookup, fetched) is { } complete
            ? Correlator.Match([entry], [complete]).FirstOrDefault()
            : null;
    }

    /// <summary>
    /// The message with its body. Asking is cheap for one already complete and expensive for a
    /// header-only file, so a download is counted exactly when the text arrived.
    /// </summary>
    private static MailFacts? Complete(MailFacts candidate, MailLookup lookup, ISet<string> fetched)
    {
        if (lookup.WithBody(candidate) is not { } complete)
        {
            return null;
        }

        if (candidate.Mail.Text.Length == 0 && complete.Mail.Text.Length > 0)
        {
            fetched.Add(candidate.Mail.Path);
        }

        return complete;
    }

    /// <summary>Entries without any evidence yet, in the order the export lists them.</summary>
    private static IEnumerable<CorrelationEntry> Unexplained(
        IReadOnlyList<CorrelationEntry> entries,
        IReadOnlyList<Match> found
    )
    {
        var explained = found.Select(match => match.Entry.Index).ToHashSet();

        return entries.Where(entry => !explained.Contains(entry.Index));
    }

    private static IReadOnlyList<MailFacts> Candidates(
        MailQuery query,
        IMailFinder finder,
        Func<string, MailFacts?> read,
        IDictionary<string, IReadOnlyList<MailFacts>> searched
    )
    {
        var key = $"{string.Join(',', query.Identifiers)}|{query.Phrase}|{query.Day:yyyy-MM-dd}";

        if (searched.TryGetValue(key, out var known))
        {
            return known;
        }

        var facts = finder.Find(query).Select(read).OfType<MailFacts>().ToArray();

        searched[key] = facts;
        return facts;
    }

    /// <summary>
    /// What the entry says about itself: the numbers a transfer carries, the merchant, and the day.
    /// A number is the strongest signal — it is what the confirmation quotes back.
    /// </summary>
    public static MailQuery QueryOf(CorrelationEntry entry)
    {
        var references = References.Of(entry);
        var window = references.Count > 0
            ? Correlator.SearchWindowDays
            : Correlator.AmountWindowDays;

        return new(references, ClueOf(entry), entry.Date, window);
    }

    /// <summary>
    /// The words a mail would carry: the merchant the entry reveals. An aggregator's token is not
    /// a merchant, so <c>PAYPAL *THOMANN</c> is searched as <c>THOMANN</c>.
    /// </summary>
    public static string ClueOf(CorrelationEntry entry) =>
        AggregatorPrefix
            .Replace(EntryPatterns.Of(entry.Payee, entry.Memo).Merchant, string.Empty)
            .Trim();

    private static readonly Regex AggregatorPrefix = new(
        @"^PAYPAL\s*\*\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
}

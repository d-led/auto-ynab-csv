using System.Globalization;
using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Correlation;

/// <summary>
/// Pairs bank entries with the mail that carries evidence for them. It never scores: a row is
/// explained when the mail quotes a number the row quotes, or when the amount and the counterparty
/// agree. Everything else is left for the report to show.
/// </summary>
public static class Correlator
{
    /// <summary>
    /// How far apart the row and the mail may be when only the amount and the counterparty agree. A
    /// processor announces the payment days before the bank books it, so this is wider than it looks.
    /// </summary>
    public const int AmountWindowDays = 5;

    /// <summary>
    /// How far the mailbox search reaches. A number the row quotes has no date: an order is booked
    /// when it is placed and the mail that lists the goods arrives when they are delivered, which
    /// can be weeks later. The window only bounds how much mail a search walks through.
    /// </summary>
    public const int SearchWindowDays = 60;

    /// <summary>The window the mailbox search uses when the row names no number to look for.</summary>
    public const int DefaultWindowDays = AmountWindowDays;

    public static IReadOnlyList<Match> Match(
        IReadOnlyList<CorrelationEntry> entries,
        IReadOnlyList<MailFacts> mails,
        int windowDays = DefaultWindowDays
    )
    {
        if (windowDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowDays), "The window cannot be negative.");
        }

        var byAmount = ByAmount(mails);
        var byReference = ByReference(mails);

        return entries
            .SelectMany(entry => MatchOne(entry, byAmount, byReference, windowDays))
            .ByEvidence()
            .ToArray();
    }

    /// <summary>
    /// The mails that mention each amount. Two years of exports against a mailbox is millions of
    /// comparisons if every entry looks at every mail; amounts are the one thing that has to be
    /// equal, so they decide who is worth looking at.
    /// </summary>
    private static IReadOnlyDictionary<decimal, List<MailFacts>> ByAmount(
        IReadOnlyList<MailFacts> mails
    )
    {
        var byAmount = new Dictionary<decimal, List<MailFacts>>();

        foreach (var mail in mails)
        {
            foreach (var amount in mail.Amounts.Distinct())
            {
                if (!byAmount.TryGetValue(amount, out var candidates))
                {
                    candidates = [];
                    byAmount[amount] = candidates;
                }

                candidates.Add(mail);
            }
        }

        return byAmount;
    }

    /// <summary>
    /// The mails that quote each reference number. A number survives a fee or a partial refund that
    /// moves the amount, so these are looked up on their own instead of through the amount.
    /// </summary>
    private static IReadOnlyDictionary<string, List<MailFacts>> ByReference(
        IReadOnlyList<MailFacts> mails
    )
    {
        var byReference = new Dictionary<string, List<MailFacts>>(StringComparer.Ordinal);

        foreach (var mail in mails)
        {
            foreach (var reference in References.In(mail))
            {
                if (!byReference.TryGetValue(reference, out var candidates))
                {
                    candidates = [];
                    byReference[reference] = candidates;
                }

                candidates.Add(mail);
            }
        }

        return byReference;
    }

    private static IEnumerable<Match> MatchOne(
        CorrelationEntry entry,
        IReadOnlyDictionary<decimal, List<MailFacts>> byAmount,
        IReadOnlyDictionary<string, List<MailFacts>> byReference,
        int windowDays
    )
    {
        foreach (var mail in Candidates(entry, byAmount, byReference))
        {
            var distance = DayDistance(entry, mail);

            // A shared number is identity, not a coincidence of dates: it means the same purchase
            // whether the mail came the day before or a month after the bank booked it.
            if (References.Shared(entry, mail) is { } reference)
            {
                yield return new Match(
                    entry,
                    mail,
                    distance,
                    new Evidence(EvidenceKind.Reference, reference)
                );

                continue;
            }

            if (distance <= windowDays && CounterpartyOf(entry, mail) is { } counterparty)
            {
                yield return new Match(
                    entry,
                    mail,
                    distance,
                    new Evidence(
                        EvidenceKind.AmountAndCounterparty,
                        $"{AmountText(entry.Amount)} · {counterparty} named"
                    )
                );
            }
        }
    }

    /// <summary>
    /// Who is worth looking at: the mails quoting a number the row quotes, plus the mails with the
    /// same amount. Both are cheap index lookups; the evidence itself is decided afterwards.
    /// </summary>
    private static IEnumerable<MailFacts> Candidates(
        CorrelationEntry entry,
        IReadOnlyDictionary<decimal, List<MailFacts>> byAmount,
        IReadOnlyDictionary<string, List<MailFacts>> byReference
    ) =>
        References
            .Of(entry)
            .SelectMany(reference => byReference.GetValueOrDefault(reference) ?? [])
            .Concat(byAmount.GetValueOrDefault(entry.Amount) ?? [])
            .DistinctBy(mail => mail.Mail.Path, StringComparer.Ordinal);

    /// <summary>An amount as evidence reads it: two places, a dot, no currency.</summary>
    public static string AmountText(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static int DayDistance(CorrelationEntry entry, MailFacts mail) =>
        Math.Abs(DateOnly.FromDateTime(mail.Mail.ReceivedAt.DateTime).DayNumber - entry.Date.DayNumber);

    /// <summary>
    /// The counterparty the row names: the vendor behind a processor, or the merchant the mail
    /// states. A name only counts when the row says it too — otherwise the amount is all there is.
    /// </summary>
    private static string? CounterpartyOf(CorrelationEntry entry, MailFacts mail) =>
        Names(entry, mail.Vendor) ?? Names(entry, mail.Fields.GetValueOrDefault("merchant"));

    private static string? Names(CorrelationEntry entry, string? name) =>
        name is { Length: > 0 } && entry.Says(name) ? name : null;
}

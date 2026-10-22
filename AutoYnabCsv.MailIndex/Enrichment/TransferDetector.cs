using AutoYnabCsv.MailIndex.Correlation;

namespace AutoYnabCsv.MailIndex.Enrichment;

/// <summary>An outgoing entry and the incoming entry in another exported account that mirrors it.</summary>
public sealed record TransferPair(CorrelationEntry Outgoing, CorrelationEntry Incoming, int DayDistance);

/// <summary>
/// Finds transfers between the exported accounts themselves. It is structural, not personal:
/// an outflow in one dropped file and an inflow of the same amount in another file, a day apart
/// at most. Nothing about your accounts is configured.
/// </summary>
public static class TransferDetector
{
    public const int DefaultDayWindow = 1;

    /// <summary>
    /// How far apart the two halves of a card settlement may be. The card account books the
    /// balance it is owed days before the current account debits it, so a settlement lands
    /// later than a move between two of your own books, which is the same day.
    /// </summary>
    public const int SettlementDayWindow = 5;

    public static IReadOnlyList<TransferPair> Find(
        IReadOnlyList<(string Source, CorrelationEntry Entry)> entries,
        int dayWindow = DefaultDayWindow
    )
    {
        var used = new HashSet<int>();
        var pairs = new List<TransferPair>();

        foreach (var (source, outgoing) in entries.Where(entry => !entry.Entry.IsInflow))
        {
            var match = entries
                .Where(candidate =>
                    candidate.Entry.IsInflow
                    && !used.Contains(candidate.Entry.Index)
                    && !string.Equals(candidate.Source, source, StringComparison.Ordinal)
                    && candidate.Entry.Amount == outgoing.Amount
                    && WithinReach(outgoing, candidate.Entry, dayWindow)
                )
                .OrderBy(candidate =>
                    Math.Abs(candidate.Entry.Date.DayNumber - outgoing.Date.DayNumber)
                )
                .ThenBy(candidate => candidate.Entry.Date)
                .FirstOrDefault();

            if (match.Entry is null)
            {
                continue;
            }

            used.Add(outgoing.Index);
            used.Add(match.Entry.Index);

            pairs.Add(
                new TransferPair(
                    outgoing,
                    match.Entry,
                    Math.Abs(match.Entry.Date.DayNumber - outgoing.Date.DayNumber)
                )
            );
        }

        return pairs;
    }

    private static bool WithinReach(
        CorrelationEntry outgoing,
        CorrelationEntry incoming,
        int dayWindow
    ) =>
        Math.Abs(incoming.Date.DayNumber - outgoing.Date.DayNumber)
        <= (BothSettleACard(outgoing, incoming) ? SettlementDayWindow : dayWindow);

    /// <summary>
    /// Both sides have to say it, so the wide window a settlement needs cannot reach an ordinary
    /// day where two equal amounts happen to sit four days apart.
    /// </summary>
    private static bool BothSettleACard(CorrelationEntry outgoing, CorrelationEntry incoming) =>
        EntryPatterns.IsCardSettlement(outgoing.Payee, outgoing.Memo)
        && EntryPatterns.IsCardSettlement(incoming.Payee, incoming.Memo);
}

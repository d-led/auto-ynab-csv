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
                    && Math.Abs(candidate.Entry.Date.DayNumber - outgoing.Date.DayNumber)
                        <= dayWindow
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
}

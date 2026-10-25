using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>A second transaction for a movement the bank shows once.</summary>
public sealed record DuplicateOf(YnabTransaction Extra, YnabTransaction Kept, int DayDistance, double Similarity);

/// <summary>
/// A re-import is how a plan collects the same movement twice: YNAB's own file import matches on
/// amount and day, and a row it fails to pair becomes a second transaction instead of an error.
/// So a leftover that shadows a matched one — same amount, a day or two apart, the same words — is
/// reported as a duplicate and not as missing money.
/// </summary>
public static class DuplicateFinder
{
    public const int DayWindow = 1;

    public const double MinimumSimilarity = 0.6;

    public static IReadOnlyList<DuplicateOf> Find(
        IReadOnlyList<Match> matches,
        IReadOnlyList<YnabTransaction> inPlanOnly
    )
    {
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<DuplicateOf>();

        foreach (var extra in inPlanOnly.Where(transaction => !transaction.IsTransfer).OrderBy(t => t.Date))
        {
            var candidate = matches
                .Select(match => match.Transaction)
                .Where(kept =>
                    !claimed.Contains(kept.Id)
                    && kept.Amount == extra.Amount
                    && Math.Abs(kept.Date.DayNumber - extra.Date.DayNumber) <= DayWindow
                )
                .Select(kept => (Kept: kept, Similarity: NameMatch.Similarity(extra.Description, kept.Description)))
                .Where(pair => pair.Similarity >= MinimumSimilarity)
                .OrderByDescending(pair => pair.Similarity)
                .ThenBy(pair => Math.Abs(pair.Kept.Date.DayNumber - extra.Date.DayNumber))
                .FirstOrDefault();

            if (candidate.Kept is null)
            {
                continue;
            }

            claimed.Add(candidate.Kept.Id);
            duplicates.Add(
                new DuplicateOf(
                    extra,
                    candidate.Kept,
                    Math.Abs(candidate.Kept.Date.DayNumber - extra.Date.DayNumber),
                    candidate.Similarity
                )
            );
        }

        return duplicates;
    }
}

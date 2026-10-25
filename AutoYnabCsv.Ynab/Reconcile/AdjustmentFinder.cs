using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>A transaction in the plan that has the shape of a correction rather than of a purchase.</summary>
public sealed record AdjustmentCandidate(YnabTransaction Transaction, string Why);

/// <summary>
/// Finds the entries somebody added to make a number agree: a payee that says so, money pushed back
/// into Ready to Assign, or — when nothing says it out loud — round amounts with no bank row behind
/// them. These are the entries a gap was papered over with, and they are the ones to replace.
/// </summary>
public static class AdjustmentFinder
{
    private static readonly string[] TellingWords =
    [
        "adjust",
        "korrektur",
        "correct",
        "anpassung",
        "ausgleich",
        "balance",
        "reconcil",
        "manual",
        "startbalance",
        "opening",
    ];

    public static IReadOnlyList<AdjustmentCandidate> Find(
        IReadOnlyList<YnabTransaction> inPlanOnly,
        YnabSnapshot snapshot
    )
    {
        var spelledOut = inPlanOnly
            .Select(transaction => new AdjustmentCandidate(transaction, Why(transaction, snapshot)))
            .Where(candidate => candidate.Why.Length > 0)
            .ToList();

        return spelledOut.Count > 0 ? spelledOut : Suspects(inPlanOnly);
    }

    /// <summary>
    /// Whether the entry says out loud that it is a correction. An entry that says so is a written
    /// record of a gap, not a purchase someone forgot to match, which is why it is never removed.
    /// </summary>
    public static bool IsSpelledOut(YnabTransaction transaction) =>
        TellingWords.Any(word =>
            NameMatch.Normalize($"{transaction.PayeeName} {transaction.Memo}")
                .Contains(word, StringComparison.Ordinal));

    private static string Why(YnabTransaction transaction, YnabSnapshot snapshot)
    {
        if (IsSpelledOut(transaction))
        {
            var said = NameMatch.Normalize($"{transaction.PayeeName} {transaction.Memo}");
            var word = TellingWords.First(entry => said.Contains(entry, StringComparison.Ordinal));
            return $"the description says it is a correction ('{word}')";
        }

        var category = snapshot.CategoryOf(transaction.CategoryId);

        return category?.IsReadyToAssign == true && string.IsNullOrWhiteSpace(transaction.PayeeName)
            ? "money put straight back into Ready to Assign"
            : string.Empty;
    }

    /// <summary>Nothing said so out loud: the round, large, unexplained amounts are the suspects.</summary>
    private static IReadOnlyList<AdjustmentCandidate> Suspects(IReadOnlyList<YnabTransaction> inPlanOnly) =>
        [.. inPlanOnly
            .Where(transaction => transaction.Amount.Amount % 10m == 0m && transaction.Amount.Abs() >= 100m)
            .Select(transaction => new AdjustmentCandidate(
                transaction,
                "a round amount with no row in the export"))];
}

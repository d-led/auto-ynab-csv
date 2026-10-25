using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>A bank row and the plan transaction that is the same movement.</summary>
public sealed record Match(BankRow Row, YnabTransaction Transaction)
{
    public int DayDistance => Math.Abs(Row.Date.DayNumber - Transaction.Date.DayNumber);
}

/// <summary>
/// The ledger as it fell: what matched, what only the bank has (a hole in the plan), and what only
/// the plan has (a duplicate, a correction, or something typed by hand). Nothing matched twice —
/// that is what makes the second and third lists mean anything.
/// </summary>
public sealed record MatchOutcome(
    IReadOnlyList<Match> Matches,
    IReadOnlyList<BankRow> InBankOnly,
    IReadOnlyList<YnabTransaction> InPlanOnly
);

/// <summary>
/// Pairs bank rows with plan transactions one to one. Amounts have to be equal to the cent; days may
/// differ by a few, because a card settles days after the purchase and a weekend shifts a posting.
/// Each pair is taken closest day first, then the most similar description first, so the best
/// candidate wins and never the first one alphabetically.
/// </summary>
public static class Matcher
{
    public const int DefaultDayWindow = 5;

    /// <summary>
    /// A card's own booking day, the day its bill is settled and the day the paying account is
    /// debited drift further apart than two current accounts do, so a card gets a longer reach.
    /// </summary>
    public const int CardDayWindow = 10;

    public static int WindowFor(YnabAccount account) =>
        string.Equals(account.Type, "creditCard", StringComparison.OrdinalIgnoreCase)
            ? CardDayWindow
            : DefaultDayWindow;

    public static MatchOutcome Match(
        IReadOnlyList<BankRow> rows,
        IReadOnlyList<YnabTransaction> transactions,
        int dayWindow = DefaultDayWindow
    )
    {
        var usedRows = new HashSet<int>();
        var usedTransactions = new HashSet<string>(StringComparer.Ordinal);
        var matches = new List<Match>();

        foreach (var (row, transaction) in Candidates(rows, transactions, dayWindow))
        {
            if (usedRows.Contains(row.Index) || usedTransactions.Contains(transaction.Id))
            {
                continue;
            }

            usedRows.Add(row.Index);
            usedTransactions.Add(transaction.Id);
            matches.Add(new Match(row, transaction));
        }

        return new MatchOutcome(
            Matches: [.. matches.OrderBy(match => match.Row.Date).ThenBy(match => match.Row.Index)],
            InBankOnly: [.. rows.Where(row => !usedRows.Contains(row.Index)).OrderBy(row => row.Date)],
            InPlanOnly: [.. transactions.Where(transaction => !usedTransactions.Contains(transaction.Id)).OrderBy(t => t.Date)]
        );
    }

    private static IEnumerable<(BankRow Row, YnabTransaction Transaction)> Candidates(
        IReadOnlyList<BankRow> rows,
        IReadOnlyList<YnabTransaction> transactions,
        int dayWindow
    ) =>
        rows.SelectMany(row =>
                transactions
                    .Where(transaction =>
                        transaction.Amount == row.Amount
                        && Math.Abs(transaction.Date.DayNumber - row.Date.DayNumber) <= dayWindow
                    )
                    .Select(transaction => (Row: row, Transaction: transaction))
            )
            .OrderBy(pair => Math.Abs(pair.Transaction.Date.DayNumber - pair.Row.Date.DayNumber))
            .ThenByDescending(pair => Similarity(pair.Row, pair.Transaction));

    /// <summary>
    /// A transfer is named after the account on the other side in the plan and after the other
    /// party in the export, so its description proves nothing either way — it stays neutral.
    /// </summary>
    private static double Similarity(BankRow row, YnabTransaction transaction) =>
        transaction.IsTransfer
            ? 0.5
            : Math.Max(
                NameMatch.Similarity(row.Payee, transaction.PayeeName),
                NameMatch.Similarity(row.Memo, transaction.Memo)
            );
}

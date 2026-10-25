using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace AutoYnabCsv.Ynab.Categorize;

/// <summary>
/// A category the plan's own history argues for, and how loud the argument is: how often this payee
/// was filed there, out of how many times it was filed anywhere.
/// </summary>
public sealed record CategoryProposal(
    YnabTransaction Transaction,
    YnabCategory Category,
    int Occurrences,
    int Filed,
    double Share
)
{
    public bool IsConfident => Occurrences >= CategoryProposals.MinimumOccurrences && Share >= CategoryProposals.MinimumShare;

    public string Why => $"filed here {Occurrences} of {Filed} times ({Share:P0})";
}

/// <summary>
/// Files an uncategorized transaction the way the same payee was filed before. Nothing is invented:
/// the answer comes from the plan's own history, and a payee filed in too many different ways, or
/// too rarely to have a habit, is left for a person.
/// </summary>
public static class CategoryProposals
{
    public const int MinimumOccurrences = 3;

    public const double MinimumShare = 0.8;

    public static IReadOnlyList<CategoryProposal> For(YnabSnapshot snapshot)
    {
        var history = History(snapshot);

        return
        [
            .. snapshot.Transactions
                .Where(NeedsACategory)
                .Select(transaction => Propose(snapshot, transaction, history))
                .OfType<CategoryProposal>()
                .OrderByDescending(proposal => proposal.Transaction.Amount.Abs())
                .ThenBy(proposal => proposal.Transaction.Date)
        ];
    }

    /// <summary>An inflow is income, and YNAB files that itself — only spending needs a category.</summary>
    private static bool NeedsACategory(YnabTransaction transaction) =>
        !transaction.IsTransfer && !transaction.IsInflow && transaction.CategoryId is null;

    private static CategoryProposal? Propose(
        YnabSnapshot snapshot,
        YnabTransaction transaction,
        IReadOnlyDictionary<string, List<YnabCategory>> history
    )
    {
        if (Key(transaction) is not { } key || !history.TryGetValue(key, out var filed))
        {
            return null;
        }

        var top = filed
            .GroupBy(category => category.Id)
            .Select(group => (Category: group.First(), Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Category.Name, StringComparer.Ordinal)
            .First();

        return new CategoryProposal(
            Transaction: transaction,
            Category: top.Category,
            Occurrences: top.Count,
            Filed: filed.Count,
            Share: (double)top.Count / filed.Count
        );
    }

    /// <summary>Every category this payee already has, in the order the plan recorded it.</summary>
    private static Dictionary<string, List<YnabCategory>> History(YnabSnapshot snapshot)
    {
        var history = new Dictionary<string, List<YnabCategory>>(StringComparer.Ordinal);

        foreach (var transaction in snapshot.Transactions.Where(transaction => !transaction.IsTransfer))
        {
            if (Key(transaction) is not { } key || snapshot.CategoryOf(transaction.CategoryId) is not { } category)
            {
                continue;
            }

            if (category.IsUncategorized || category.Internal)
            {
                continue;
            }

            if (!history.TryGetValue(key, out var filed))
            {
                filed = [];
                history[key] = filed;
            }

            filed.Add(category);
        }

        return history;
    }

    private static string? Key(YnabTransaction transaction) =>
        !string.IsNullOrWhiteSpace(transaction.PayeeId)
            ? $"id:{transaction.PayeeId}"
            : KeyOfName(transaction.PayeeName);

    private static string? KeyOfName(string? payee)
    {
        var normalized = NameMatch.Normalize(payee);
        return normalized.Length == 0 ? null : $"name:{normalized}";
    }
}

using System.Security.Cryptography;
using System.Text;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace AutoYnabCsv.Ynab.Apply;

/// <summary>A row the plan is missing, and the entry that would put it there.</summary>
public sealed record PlannedCreate(
    string AccountId,
    string AccountName,
    DateOnly Date,
    Money Amount,
    string PayeeName,
    string Memo,
    string ImportId,
    string? CategoryId,
    string? TransferAccountId = null
);

/// <summary>An entry the export does not show, to be removed — with what it was, so it can come back.</summary>
public sealed record PlannedDelete(string AccountName, YnabTransaction Transaction, string Why);

/// <summary>A category for a transaction that stays exactly where it is.</summary>
public sealed record PlannedCategory(string TransactionId, string CategoryId, string CategoryName, string Why);

/// <summary>
/// What applying will do, in the order it says. A plan is a file a person can read before anything
/// changes, and re-applying it is safe because every creation carries the same import id it had the
/// first time.
/// </summary>
public sealed record RepairPlan(
    string PlanId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PlannedCreate> Creates,
    IReadOnlyList<PlannedDelete> Deletes,
    IReadOnlyList<PlannedCategory> Categories,
    IReadOnlyList<string> Notes
)
{
    public bool IsEmpty => Creates.Count == 0 && Deletes.Count == 0 && Categories.Count == 0;

    public int Total => Creates.Count + Deletes.Count + Categories.Count;
}

/// <summary>
/// Turns a reconciliation into the smallest set of changes that makes the two ledgers agree: the
/// rows only the bank has are added, the corrections that held a gap shut are removed, and the
/// categories the history argues for are set. Nothing is changed that was not asked for.
/// </summary>
public static class RepairPlanBuilder
{
    public static RepairPlan From(
        Reconciliation reconciliation,
        IReadOnlyList<Categorize.CategoryProposal> proposals,
        bool repairBalances = true,
        bool categorize = true,
        bool deleteDuplicates = true,
        bool deleteExtras = true
    )
    {
        IReadOnlyList<PlannedCreate> creates = repairBalances ? Creates(reconciliation) : [];
        IReadOnlyList<PlannedDelete> deletes = repairBalances ? Deletes(reconciliation, deleteDuplicates, deleteExtras) : [];
        IReadOnlyList<PlannedCategory> categories = categorize
            ? [.. proposals.Where(proposal => proposal.IsConfident).Select(proposal => new PlannedCategory(
                TransactionId: proposal.Transaction.Id,
                CategoryId: proposal.Category.Id,
                CategoryName: proposal.Category.Name,
                Why: proposal.Why))]
            : [];

        return new RepairPlan(
            PlanId: reconciliation.Snapshot.PlanId,
            CreatedAt: DateTimeOffset.UtcNow,
            Creates: creates,
            Deletes: deletes,
            Categories: categories,
            Notes: Notes(reconciliation)
        );
    }

    /// <summary>
    /// A movement between two of your own accounts is one row to add, not two: creating both legs
    /// separately would double the money, and creating one as a transfer lets YNAB write the other.
    /// </summary>
    private static IReadOnlyList<PlannedCreate> Creates(Reconciliation reconciliation)
    {
        var missing = reconciliation.Accounts
            .SelectMany(account => account.MissingInPlan.Select(row => (Account: account.Account, Row: row)))
            .ToList();

        var creates = new List<PlannedCreate>();
        var handled = new HashSet<BankRow>();

        foreach (var transfer in reconciliation.Transfers)
        {
            var from = missing.FirstOrDefault(pair => pair.Row == transfer.Outgoing);
            var to = missing.FirstOrDefault(pair => pair.Row == transfer.Incoming);

            if (from.Row is null || to.Row is null)
            {
                continue;
            }

            creates.Add(Create(from.Account, from.Row, to.Account.Id));
            handled.Add(from.Row);
            handled.Add(to.Row);
        }

        creates.AddRange(
            missing.Where(pair => !handled.Contains(pair.Row)).Select(pair => Create(pair.Account, pair.Row, transferAccountId: null))
        );

        return [.. creates.OrderBy(create => create.Date)];
    }

    private static PlannedCreate Create(YnabAccount account, BankRow row, string? transferAccountId) =>
        new(
            AccountId: account.Id,
            AccountName: account.Name,
            Date: row.Date,
            Amount: row.Amount,
            PayeeName: transferAccountId is null ? row.Payee : string.Empty,
            Memo: row.Memo,
            ImportId: ImportIds.Of(account.Id, row.Date, row.Amount, row.Payee, row.Memo),
            CategoryId: null,
            TransferAccountId: transferAccountId
        );

    private static IReadOnlyList<PlannedDelete> Deletes(
        Reconciliation reconciliation,
        bool deleteDuplicates,
        bool deleteExtras
    )
    {
        var deletes = new List<PlannedDelete>();

        // A correction is an extra like any other; it is only named better above.
        if (deleteExtras)
        {
            deletes.AddRange(
                reconciliation.Accounts.SelectMany(account => account.OnlyInPlan.Select(transaction =>
                    new PlannedDelete(account.Account.Name, transaction, "the export does not have this row")))
            );
        }
        else
        {
            deletes.AddRange(
                reconciliation.Accounts.SelectMany(account => account.Adjustments.Select(adjustment =>
                    new PlannedDelete(account.Account.Name, adjustment.Transaction, adjustment.Why)))
            );
        }

        if (deleteDuplicates)
        {
            deletes.AddRange(
                reconciliation.Accounts.SelectMany(account => account.Duplicates.Select(duplicate =>
                    new PlannedDelete(
                        account.Account.Name,
                        duplicate.Extra,
                        $"a second copy of a movement the export shows once (kept '{duplicate.Kept.Description}')")))
            );
        }

        var unique = deletes
            .GroupBy(delete => delete.Transaction.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        // YNAB deletes both legs of a transfer with one delete, so an incoming leg is dropped when
        // its outgoing partner is being deleted too: the second call would find nothing.
        var outgoing = unique
            .Where(delete => delete.Transaction.IsTransfer && delete.Transaction.Amount.IsNegative)
            .Select(delete => (delete.Transaction.Date, Amount: delete.Transaction.Amount.Abs()))
            .ToHashSet();

        return
        [
            .. unique
                .Where(delete =>
                    !delete.Transaction.IsTransfer
                    || delete.Transaction.Amount.IsNegative
                    || !outgoing.Contains((delete.Transaction.Date, delete.Transaction.Amount.Abs())))
                .OrderBy(delete => delete.Transaction.Date)
        ];
    }

    private static IReadOnlyList<string> Notes(Reconciliation reconciliation)
    {
        var notes = new List<string>();

        notes.AddRange(
            reconciliation.Accounts
                .Where(account => account.Balances == false)
                .Select(account => $"{account.Label}: off by {account.Difference}, repair moves it by {account.RepairDelta}")
        );

        if (reconciliation.Transfers.Count > 0)
        {
            notes.Add(
                $"{reconciliation.Transfers.Count} movement(s) between two exported accounts are missing on both sides; "
                    + "each is added once, as a transfer"
            );
        }

        notes.AddRange(
            reconciliation.Accounts
                .Where(account => account.Duplicates.Count > 0)
                .Select(account => $"{account.Label}: {account.Duplicates.Count} duplicate(s) removed")
        );

        notes.AddRange(reconciliation.Unresolved.Select(statement => $"Unresolved export: {statement.Label}"));

        return notes;
    }
}

/// <summary>
/// An import id the API accepts and the plan can recognise: stable for the same movement, so a
/// second apply finds its own work instead of piling a copy on top of it.
/// </summary>
public static class ImportIds
{
    /// <summary>YNAB allows 36 characters; this uses 25.</summary>
    public static string Of(string accountId, DateOnly date, Money amount, string payee, string memo)
    {
        var raw = string.Join(
            '|',
            accountId,
            date.ToString("yyyy-MM-dd"),
            amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            NameMatch.Normalize(payee),
            NameMatch.Normalize(memo)
        );
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..12].ToLowerInvariant();

        return $"ayc-{date:yyyyMMdd}-{hash}";
    }
}

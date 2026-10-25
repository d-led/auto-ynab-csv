using System.Security.Cryptography;
using System.Text;
using AutoYnabCsv.Contracts;
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
/// A transaction that belongs to a different account than the one it was entered in. One PATCH moves
/// it; deleting and recreating it would touch two accounts and lose its category and its memo.
/// </summary>
public sealed record PlannedMove(
    string TransactionId,
    string FromAccountId,
    string ToAccountId,
    string FromLabel,
    string ToLabel,
    DateOnly Date,
    Money Amount,
    string Description
);

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
    IReadOnlyList<string> Notes,
    IReadOnlyList<PlannedMove> Moves
)
{
    public bool IsEmpty => Creates.Count == 0 && Deletes.Count == 0 && Categories.Count == 0 && Moves.Count == 0;

    public int Total => Creates.Count + Deletes.Count + Categories.Count + Moves.Count;
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
        var consolidated = repairBalances ? Consolidated(reconciliation) : ([], []);
        IReadOnlyList<PlannedDelete> deletes = repairBalances
            ? [.. Deletes(reconciliation, deleteDuplicates, deleteExtras), .. consolidated.Item1]
            : [];
        IReadOnlyList<PlannedCreate> creates = repairBalances
            ? [.. Creates(reconciliation), .. Rescued(reconciliation, deletes), .. consolidated.Item2]
            : [];
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
            Notes: Notes(reconciliation),
            Moves: repairBalances ? Moves(reconciliation) : []
        );
    }

    /// <summary>
    /// Several rows that all say they are a balance adjustment are one thing written down more than
    /// once. They are replaced by a single row carrying their sum, so the balance does not move and
    /// the account stops reading like a pile of excuses. A set that cancels out is simply removed.
    /// </summary>
    private static (IReadOnlyList<PlannedDelete> Deletes, IReadOnlyList<PlannedCreate> Creates) Consolidated(
        Reconciliation reconciliation
    )
    {
        var deletes = new List<PlannedDelete>();
        var creates = new List<PlannedCreate>();

        foreach (var account in reconciliation.Accounts)
        {
            var adjustments = account
                .OnlyInPlan.Where(Reconcile.AdjustmentFinder.IsSpelledOut)
                .OrderBy(transaction => transaction.Date)
                .ToList();
            if (adjustments.Count < 2)
            {
                continue;
            }

            var sum = new Money(adjustments.Sum(transaction => transaction.Amount.Amount));
            var latest = adjustments[^1];
            var reason = $"one of {adjustments.Count} adjustments, written again as one row";

            deletes.AddRange(adjustments.Select(transaction => new PlannedDelete(account.Account.Name, transaction, reason)));

            if (!sum.IsZero)
            {
                var memo = $"{adjustments.Count} adjustments consolidated";
                creates.Add(
                    new PlannedCreate(
                        AccountId: account.Account.Id,
                        AccountName: account.Account.Name,
                        Date: latest.Date,
                        Amount: sum,
                        PayeeName: "Balance adjustment",
                        Memo: memo,
                        ImportId: ImportIds.Of(account.Account.Id, latest.Date, sum, "Balance adjustment", memo),
                        CategoryId: null
                    )
                );
            }
        }

        return (deletes, creates);
    }

    /// <summary>
    /// A transfer has two legs and YNAB removes both when one is deleted. When the other half is a row
    /// the bank shows, that half goes too — so it is written again, as an ordinary row, because the
    /// transfer it was half of is not going to exist any more. Without this, deleting a wrong transfer
    /// takes the right row with it, and the next reconcile asks for that row back: the same numbers,
    /// forever.
    ///
    /// A half the exports do not show either is not written again: writing it back would invent a
    /// movement the bank never had, and the next reconcile would delete it. That is the loop.
    /// </summary>
    private static IReadOnlyList<PlannedCreate> Rescued(
        Reconciliation reconciliation,
        IReadOnlyList<PlannedDelete> deletes
    )
    {
        var victims = deletes.Select(delete => delete.Transaction.Id).ToHashSet(StringComparer.Ordinal);
        var absentFromTheExports = reconciliation
            .Accounts.SelectMany(account => account.OnlyInPlan)
            .Select(transaction => transaction.Id)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. deletes
                .Select(delete => delete.Transaction)
                .Where(victim => victim.IsTransfer && victim.TransferAccountId is not null)
                .Select(victim => PartnerOf(victim, reconciliation.Snapshot.Transactions))
                .OfType<YnabTransaction>()
                .Where(partner => !victims.Contains(partner.Id))
                .Where(partner => !absentFromTheExports.Contains(partner.Id))
                .Select(partner => CreateOf(partner, reconciliation.Snapshot.Accounts))
                .DistinctBy(create => create.ImportId, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// The other half of a transfer: same day, opposite amount, in the account the leg names. YNAB keeps
    /// both halves on one day, which is what makes the partner findable at all.
    /// </summary>
    private static YnabTransaction? PartnerOf(YnabTransaction victim, IReadOnlyList<YnabTransaction> transactions) =>
        transactions.FirstOrDefault(candidate =>
            candidate.Id != victim.Id
            && candidate.Date == victim.Date
            && candidate.AccountId == victim.TransferAccountId
            && candidate.Amount == -victim.Amount
        );

    /// <summary>The row as an ordinary one, written again under an import id of ours.</summary>
    private static PlannedCreate CreateOf(YnabTransaction partner, IReadOnlyList<YnabAccount> accounts) =>
        new(
            AccountId: partner.AccountId,
            AccountName: partner.AccountName,
            Date: partner.Date,
            Amount: partner.Amount,
            PayeeName: TransferNaming.WithoutPrefix(partner.PayeeName ?? string.Empty),
            Memo: partner.Memo ?? string.Empty,
            ImportId: ImportIds.Of(
                partner.AccountId,
                partner.Date,
                partner.Amount,
                TransferNaming.WithoutPrefix(partner.PayeeName ?? string.Empty),
                partner.Memo ?? string.Empty
            ),
            CategoryId: partner.CategoryId
        );

    /// <summary>
    /// A row that is not a transfer any more cannot be paid to a transfer: the API refuses a payee that
    /// starts with an internal one, so the far side's name is used as it is.
    /// </summary>
    private static string Plain(string payee) => TransferNaming.WithoutPrefix(payee);

    /// <summary>
    /// A row the bank has in another account is moved there instead of being added here and removed
    /// there: one change, and the category and memo the entry already carries survive it.
    /// </summary>
    private static IReadOnlyList<PlannedMove> Moves(Reconciliation reconciliation)
    {
        var idOfLabel = reconciliation.Accounts.ToDictionary(
            account => account.Label,
            account => account.Account.Id,
            StringComparer.Ordinal
        );

        return
        [
            .. reconciliation.Reaccountings
                .Where(move => idOfLabel.ContainsKey(move.FromLabel) && idOfLabel.ContainsKey(move.ToLabel))
                .Select(move => new PlannedMove(
                    TransactionId: move.Transaction.Id,
                    FromAccountId: idOfLabel[move.FromLabel],
                    ToAccountId: idOfLabel[move.ToLabel],
                    FromLabel: move.FromLabel,
                    ToLabel: move.ToLabel,
                    Date: move.Transaction.Date,
                    Amount: move.Transaction.Amount,
                    Description: move.Transaction.Description))
                .OrderBy(move => move.Date)
        ];
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

        // A row that is only in another account is moved there, not created here.
        var moved = reconciliation.Reaccountings.Select(move => move.Row).ToHashSet();

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
            missing
                .Where(pair => !handled.Contains(pair.Row) && !moved.Contains(pair.Row))
                .Select(pair => Create(pair.Account, pair.Row, transferAccountId: null))
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

        // An entry that says it is a balance adjustment is the written record of a gap with no rows
        // behind it. Removing it does not tidy anything up — it puts the gap back, and the next
        // reconcile asks for the same adjustment again. It stays.
        var adjustments = reconciliation
            .Accounts.SelectMany(account => account.OnlyInPlan)
            .Where(AdjustmentFinder.IsSpelledOut)
            .Select(transaction => transaction.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (deleteExtras)
        {
            deletes.AddRange(
                reconciliation.Accounts.SelectMany(account =>
                    account
                        .OnlyInPlan.Where(transaction => !adjustments.Contains(transaction.Id))
                        .Select(transaction => new PlannedDelete(
                            account.Account.Name,
                            transaction,
                            "the export does not have this row")))
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
            .Where(delete => !reconciliation.Reaccountings.Any(move => move.Transaction.Id == delete.Transaction.Id))
            .ToList();

        // YNAB removes both legs of a transfer with one delete, so the second call for a pair finds
        // nothing; the applier treats that as done rather than as a failure. Every row is still asked
        // for once, because a pair with a duplicated leg has more legs than one delete can take.
        return [.. unique.OrderBy(delete => delete.Transaction.Date)];
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

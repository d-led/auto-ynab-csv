using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>
/// One account, both ledgers, and the argument between them. Everything here is about a single
/// account, because that is the only place where a difference means anything.
/// </summary>
public sealed record AccountReconciliation(
    YnabAccount Account,
    BankStatement Statement,
    DateOnly Cutoff,
    Money? ExportBalance,
    Money PlanBalanceAtCutoff,
    IReadOnlyList<Match> Matches,
    IReadOnlyList<BankRow> MissingInPlan,
    IReadOnlyList<YnabTransaction> OnlyInPlan,
    IReadOnlyList<DuplicateOf> Duplicates,
    IReadOnlyList<AdjustmentCandidate> Adjustments,
    IReadOnlyList<BankRow> ZeroAmountRows,
    IReadOnlyDictionary<int, string> TransferTargets,
    GapReport? Gap,
    bool BalanceFromExport = true
)
{
    public string Label => Statement.Label;

    public string SourceFile => Statement.SourceFile;

    /// <summary>The account this row is a transfer to, when the export prints one that is ours.</summary>
    public string? TransferTargetOf(BankRow row) => TransferTargets.GetValueOrDefault(row.Index);

    /// <summary>What the plan holds beyond what the bank says, on the day the export was written.</summary>
    public Money? Difference => ExportBalance is null ? null : PlanBalanceAtCutoff - ExportBalance.Value;

    public bool? Balances => Difference is null ? null : Difference.Value.IsZero;

    /// <summary>
    /// What the repair would do to the balance: the rows only the bank has are added, the ones only
    /// the plan has are removed, and a duplicate is a plan row that goes too — so it counts here,
    /// even though it is reported apart from the unexplained extras.
    /// </summary>
    public Money RepairDelta =>
        Money.Sum(MissingInPlan.Select(row => row.Amount))
        - Money.Sum(OnlyInPlan.Select(transaction => transaction.Amount))
        - Money.Sum(Duplicates.Select(duplicate => duplicate.Extra.Amount));

    public Money? DifferenceAfterRepair => Difference + RepairDelta;
}

/// <summary>The whole of it: every account that could be paired, and every export that could not.</summary>
public sealed record Reconciliation(
    YnabSnapshot Snapshot,
    IReadOnlyList<AccountReconciliation> Accounts,
    IReadOnlyList<BankStatement> Unresolved,
    IReadOnlyList<Transfer> Transfers
)
{
    public Money TotalRepairDelta => Money.Sum(Accounts.Select(account => account.RepairDelta));

    public bool AnyMismatch => Accounts.Any(account => account.Balances == false);
}

public static class Reconciler
{
    public static Reconciliation Of(
        YnabSnapshot snapshot,
        IReadOnlyList<BankStatement> statements,
        IReadOnlyDictionary<string, string>? overrides = null,
        IReadOnlyDictionary<string, string>? ownedIbans = null,
        IReadOnlyDictionary<string, Money>? expectedBalances = null
    )
    {
        var mapping = AccountMappingResolver.Resolve(
            statements,
            snapshot.Accounts,
            overrides ?? new Dictionary<string, string>()
        );
        var own = OwnAccounts.Of(statements, ownedIbans);
        var accounts = mapping.Assignments
            .Select(assignment =>
                For(
                    snapshot,
                    assignment,
                    own,
                    Expected(assignment.Statement.Label, expectedBalances)
                )
            )
            .ToList();
        var missing = accounts
            .SelectMany(account => account.MissingInPlan.Select(row => new RowOf(account.Label, row)))
            .ToList();

        return new Reconciliation(
            Snapshot: snapshot,
            Accounts: accounts,
            Unresolved: mapping.Unresolved,
            Transfers: TransferFinder.Pair(missing, own)
        );
    }

    private static Money? Expected(string label, IReadOnlyDictionary<string, Money>? expectedBalances) =>
        expectedBalances is not null && expectedBalances.TryGetValue(label, out var expected) ? expected : null;

    public static AccountReconciliation For(
        YnabSnapshot snapshot,
        AccountAssignment assignment,
        OwnAccounts? own = null,
        Money? expectedBalance = null
    )
    {
        var statement = assignment.Statement;
        var account = assignment.Account;
        var cutoff = Cutoff(statement);
        var window = Matcher.WindowFor(account);

        // The plan can hold a movement days before the oldest row the export prints — a card is
        // booked when it is charged and settled later — so the period starts a window earlier.
        var from =
            statement.Statement.PeriodFrom
            ?? (statement.Rows.Count > 0 ? statement.Rows.Min(row => row.Date).AddDays(-window) : DateOnly.MinValue);

        var zeroAmountRows = statement.Rows.Where(row => row.IsZero).ToList();
        var rows = statement.Rows.Where(row => !row.IsZero).ToList();
        var transactions = snapshot.TransactionsOf(account.Id)
            .Where(transaction => transaction.Date >= from && transaction.Date <= cutoff)
            .ToList();

        var matched = Matcher.Match(rows, transactions, window);
        var duplicates = DuplicateFinder.Find(matched.Matches, matched.InPlanOnly);
        var duplicateIds = duplicates.Select(duplicate => duplicate.Extra.Id).ToHashSet(StringComparer.Ordinal);
        var onlyInPlan = matched.InPlanOnly
            .Where(transaction => !duplicateIds.Contains(transaction.Id))
            .Where(transaction => !transaction.Amount.IsZero)
            .ToList();
        var planBalance = snapshot.BalanceAt(account.Id, cutoff);

        var bankBalance = statement.ClosingBalance ?? expectedBalance;

        return new AccountReconciliation(
            Account: account,
            Statement: statement,
            Cutoff: cutoff,
            ExportBalance: bankBalance,
            PlanBalanceAtCutoff: planBalance,
            Matches: matched.Matches,
            MissingInPlan: matched.InBankOnly,
            OnlyInPlan: onlyInPlan,
            Duplicates: duplicates,
            Adjustments: AdjustmentFinder.Find(onlyInPlan, snapshot),
            ZeroAmountRows: zeroAmountRows,
            TransferTargets: TransferFinder.TargetsOf(statement, own ?? OwnAccounts.Empty),
            Gap: bankBalance is { } closing
                ? GapTimeline.Of(rows, transactions, cutoff, closing, planBalance)
                : null,
            BalanceFromExport: statement.ClosingBalance is not null
        );
    }

    private static DateOnly Cutoff(BankStatement statement) =>
        statement.ClosingDate ?? statement.LastRowDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
}

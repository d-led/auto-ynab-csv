namespace AutoYnabCsv.Ynab.Contracts;

/// <summary>
/// Everything the plan said when it was read: accounts, categories, payees and every transaction
/// since the start of the export. It is the offline copy all the comparisons run against, so a
/// question about a balance never costs a request and never sees a half-updated plan.
/// </summary>
public sealed record YnabSnapshot(
    string PlanId,
    string PlanName,
    string CurrencyFormat,
    DateTimeOffset FetchedAt,
    IReadOnlyList<YnabAccount> Accounts,
    IReadOnlyList<YnabCategory> Categories,
    IReadOnlyList<YnabPayee> Payees,
    IReadOnlyList<YnabTransaction> Transactions
)
{
    public YnabAccount? AccountOf(string accountId) =>
        Accounts.FirstOrDefault(account => account.Id == accountId);

    public IReadOnlyList<YnabTransaction> TransactionsOf(string accountId) =>
        [.. Transactions.Where(transaction => transaction.AccountId == accountId).OrderBy(t => t.Date)];

    /// <summary>
    /// The balance the account had on the day the export was written: what YNAB holds now, less
    /// everything booked after that day. This is the number the export's own balance is held to,
    /// so a bank row arriving after the export cannot look like a difference.
    /// </summary>
    public Money BalanceAt(string accountId, DateOnly day)
    {
        var bookedAfter = Money.Sum(
            Transactions
                .Where(transaction => transaction.AccountId == accountId && transaction.Date > day)
                .Select(transaction => transaction.Amount)
        );
        var account = AccountOf(accountId) ?? throw new AccountNotInSnapshotException(accountId);

        return account.Balance - bookedAfter;
    }

    public YnabCategory? CategoryOf(string? categoryId) =>
        categoryId is null ? null : Categories.FirstOrDefault(category => category.Id == categoryId);

    public YnabCategory? CategoryNamed(string name) =>
        Categories.FirstOrDefault(category =>
            string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)
        );
}

[Serializable]
public sealed class AccountNotInSnapshotException(string accountId)
    : Exception($"Account {accountId} is not in the snapshot.");

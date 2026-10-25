namespace AutoYnabCsv.Ynab.Contracts;

/// <summary>
/// One booked movement in the plan. A transfer carries the account on the other side, which is
/// what makes it distinguishable from a payment to a person with the same amount and day.
/// </summary>
public sealed record YnabTransaction(
    string Id,
    DateOnly Date,
    Money Amount,
    string AccountId,
    string AccountName,
    string? PayeeId,
    string? PayeeName,
    string? CategoryId,
    string? CategoryName,
    string? Memo,
    bool Cleared,
    bool Approved,
    string? TransferAccountId,
    string? ImportId
)
{
    public bool IsTransfer => TransferAccountId is not null;

    public bool IsInflow => Amount.Sign > 0;

    /// <summary>What a person would call this row, for matching and for a report to read.</summary>
    public string Description =>
        !string.IsNullOrWhiteSpace(PayeeName) ? PayeeName!
        : !string.IsNullOrWhiteSpace(Memo) ? Memo!
        : string.Empty;
}

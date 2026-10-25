namespace AutoYnabCsv.Statements;

/// <summary>
/// What the header of an export says about the account it belongs to: the name and identifier the
/// bank printed, the period it covers, and the balance it declares for its last day. These are the
/// numbers the reconciliation holds YNAB against, so they are read rather than inferred.
/// </summary>
public sealed record ExportStatement(
    string AccountName,
    string AccountIdentifier,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    DateOnly? BalanceDate,
    decimal? ClosingBalance
)
{
    public static readonly ExportStatement Unknown = new(
        AccountName: string.Empty,
        AccountIdentifier: string.Empty,
        PeriodFrom: null,
        PeriodTo: null,
        BalanceDate: null,
        ClosingBalance: null
    );
}

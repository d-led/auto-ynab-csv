namespace AutoYnabCsv.Ynab.Contracts;

/// <summary>An account in the plan, with the three balances YNAB keeps for it.</summary>
public sealed record YnabAccount(
    string Id,
    string Name,
    string Type,
    bool OnBudget,
    bool Closed,
    Money Balance,
    Money Cleared,
    Money Uncleared
)
{
    /// <summary>Debt accounts, which YNAB treats as liabilities and budgets differently.</summary>
    private static readonly string[] DebtTypes =
    [
        "creditCard",
        "lineOfCredit",
        "mortgage",
        "autoLoan",
        "studentLoan",
        "personalLoan",
        "medicalDebt",
        "otherDebt",
    ];

    public bool IsDebt => DebtTypes.Contains(Type, StringComparer.OrdinalIgnoreCase);
}

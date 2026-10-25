using System.Globalization;
using AutoYnabCsv.Ynab;
using AutoYnabCsv.Ynab.Contracts;

namespace Tests.Ynab;

/// <summary>A plan with a little history in it, so the tests argue about behaviour and not about
/// constructing records.</summary>
internal static class Sample
{
    public static readonly YnabAccount Giro = new(
        "acc-giro",
        "Girokonto · 9004",
        "checking",
        OnBudget: true,
        Closed: false,
        Balance: Money.Zero,
        Cleared: Money.Zero,
        Uncleared: Money.Zero
    );

    public static readonly YnabCategory Groceries = new("cat-groceries", "Groceries", "Everyday", false);

    public static readonly YnabCategory Dining = new("cat-dining", "Dining out", "Everyday", false);

    public static readonly YnabCategory Rent = new("cat-rent", "Rent", "Home", false);

    public static readonly YnabCategory ReadyToAssign = new(
        "cat-rta",
        YnabCategory.ReadyToAssignName,
        "Internal Master Category",
        true
    );

    public static YnabSnapshot Snapshot(params YnabTransaction[] transactions) =>
        new(
            PlanId: "plan-1",
            PlanName: "Household",
            CurrencyFormat: "EUR",
            FetchedAt: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
            Accounts: [Giro],
            Categories: [Groceries, Dining, Rent, ReadyToAssign],
            Payees: [],
            Transactions: transactions
        );

    public static YnabTransaction T(
        string id,
        string day,
        decimal amount,
        string payee,
        YnabCategory? category = null,
        string? transferAccountId = null
    ) =>
        new(
            Id: id,
            Date: DateOnly.Parse(day, CultureInfo.InvariantCulture),
            Amount: amount,
            AccountId: Giro.Id,
            AccountName: Giro.Name,
            PayeeId: null,
            PayeeName: payee,
            CategoryId: category?.Id,
            CategoryName: category?.Name,
            Memo: null,
            Cleared: true,
            Approved: true,
            TransferAccountId: transferAccountId,
            ImportId: null
        );
}

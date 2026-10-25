using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class ReconcilerWindowTest
{
    private static readonly YnabAccount Visa = new(
        "acc-visa",
        "DKB Visa New",
        "creditCard",
        OnBudget: true,
        Closed: false,
        Balance: Money.Zero,
        Cleared: Money.Zero,
        Uncleared: Money.Zero
    );

    private static readonly YnabAccount Giro = new(
        "acc-giro",
        "DKB Giro",
        "checking",
        OnBudget: true,
        Closed: false,
        Balance: Money.Zero,
        Cleared: Money.Zero,
        Uncleared: Money.Zero
    );

    [TestMethod]
    public void ACardBillSettledNineDaysLateIsStillTheSameMovement()
    {
        var reconciled = Reconcile(
            Visa,
            rows: [Row("2026-10-22", -9.99m), Row("2026-09-01", -2.49m)],
            transactions: [Plan(Visa, "t1", "2026-08-23", -2.49m, "Kartenpreis")]
        );

        Assert.AreEqual(1, reconciled.Matches.Count, "a card's booking day drifts further than a current account's");
        Assert.AreEqual(0, reconciled.OnlyInPlan.Count);
    }

    [TestMethod]
    public void ATransactionOfNothingIsNotUnexplainedMoney()
    {
        var reconciled = Reconcile(
            Giro,
            rows: [Row("2026-10-22", -9.99m)],
            transactions: [Plan(Giro, "t1", "2026-10-22", -9.99m, "Shop"), Plan(Giro, "t2", "2025-12-30", 0m, "Transfer : Giro Emergency")]
        );

        Assert.AreEqual(1, reconciled.Matches.Count);
        Assert.AreEqual(0, reconciled.OnlyInPlan.Count, "a zero-amount row can never be money the export forgot");
    }

    private static AccountReconciliation Reconcile(
        YnabAccount account,
        IReadOnlyList<BankRow> rows,
        IReadOnlyList<YnabTransaction> transactions
    )
    {
        var snapshot = new YnabSnapshot(
            PlanId: "plan-1",
            PlanName: "New Budget",
            CurrencyFormat: "EUR",
            FetchedAt: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
            Accounts: [account],
            Categories: [],
            Payees: [],
            Transactions: transactions
        );

        var statement = new BankStatement(
            "export.csv",
            account.Name,
            new ExportStatement(account.Name, string.Empty, null, null, new DateOnly(2026, 10, 25), null),
            rows
        );

        return Reconciler.For(snapshot, new AccountAssignment(statement, account, "test"));
    }

    private static BankRow Row(string day, decimal amount) =>
        new(DateOnly.Parse(day), amount, "Shop", "memo", "export.csv", RowIndex++);

    private static int RowIndex { get; set; }

    private static YnabTransaction Plan(YnabAccount account, string id, string day, decimal amount, string payee) =>
        new(
            Id: id,
            Date: DateOnly.Parse(day),
            Amount: amount,
            AccountId: account.Id,
            AccountName: account.Name,
            PayeeId: null,
            PayeeName: payee,
            CategoryId: null,
            CategoryName: null,
            Memo: null,
            Cleared: true,
            Approved: true,
            TransferAccountId: null,
            ImportId: null
        );
}

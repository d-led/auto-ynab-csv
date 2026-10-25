using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class ExpectedBalanceTest
{
    private static readonly YnabAccount N26 = new(
        "acc-n26",
        "N26",
        "creditCard",
        OnBudget: true,
        Closed: false,
        Balance: 4339.89m,
        Cleared: Money.Zero,
        Uncleared: Money.Zero
    );

    [TestMethod]
    public void ABalanceToldByHandStandsInForOneTheExportDoesNotPrint()
    {
        var reconciled = Reconcile(expectedBalance: -25.00m);

        Assert.AreEqual(-25.00m, reconciled.ExportBalance, "the app says minus 25,00");
        Assert.IsFalse(reconciled.BalanceFromExport, "and the report says where it came from");
    }

    [TestMethod]
    public void TheDifferenceIsAgainstTheBalanceToldByHandNotAgainstNothing()
    {
        var reconciled = Reconcile(expectedBalance: -25.00m);

        Assert.AreEqual(4364.89m, reconciled.Difference, "the plan thinks it holds 4 339,89 more than the bank does");
        Assert.IsFalse(reconciled.Balances);
    }

    [TestMethod]
    public void WithoutABalanceTheAccountCannotBeBlamed()
    {
        var reconciled = Reconcile(expectedBalance: null);

        Assert.IsNull(reconciled.ExportBalance);
        Assert.IsNull(reconciled.Difference);
        Assert.IsNull(reconciled.Balances);
    }

    private static AccountReconciliation Reconcile(Money? expectedBalance)
    {
        var snapshot = new YnabSnapshot(
            PlanId: "plan-1",
            PlanName: "New Budget",
            CurrencyFormat: "EUR",
            FetchedAt: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
            Accounts: [N26],
            Categories: [],
            Payees: [],
            Transactions: []
        );

        var statement = new BankStatement(
            "n26.csv",
            "n26",
            new ExportStatement("Main Account", string.Empty, null, null, null, null),
            [new BankRow(new DateOnly(2026, 10, 23), -1.30m, "Voi", "Online", "n26.csv", 0)]
        );

        return Reconciler.For(snapshot, new AccountAssignment(statement, N26, "test"), null, expectedBalance);
    }
}

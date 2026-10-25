using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class RepairPlanTransferTest
{
    private static readonly YnabAccount Giro = Account("acc-giro", "DKB Giro");

    private static readonly YnabAccount Together = Account("acc-together", "GiroTogether");

    [TestMethod]
    public void AMovementBetweenTwoAccountsIsAddedOnceAsATransfer()
    {
        var outgoing = Row(0, "2026-01-14", -218m, "Girokonto · 4781");
        var incoming = Row(1, "2026-01-14", 218m, "Girokonto · 3784");

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(
                [AccountReconciliationOf(Giro, outgoing), AccountReconciliationOf(Together, incoming)],
                [new Transfer(outgoing, incoming, "Girokonto · 4781", "Girokonto · 3784", 0, ConfirmedByAccountNumber: true)]
            ),
            []
        );

        Assert.AreEqual(1, plan.Creates.Count, "one transfer, not two rows");
        Assert.AreEqual("acc-together", plan.Creates[0].TransferAccountId);
        Assert.AreEqual(string.Empty, plan.Creates[0].PayeeName, "YNAB names a transfer itself");
    }

    [TestMethod]
    public void TwoUnrelatedRowsAreAddedSeparately()
    {
        var outgoing = Row(0, "2026-01-14", -218m, "Someone");
        var incoming = Row(1, "2026-01-14", 218m, "Someone else");

        var plan = RepairPlanBuilder.From(
            ReconciliationWith([AccountReconciliationOf(Giro, outgoing), AccountReconciliationOf(Together, incoming)]),
            []
        );

        Assert.AreEqual(2, plan.Creates.Count);
        Assert.IsTrue(plan.Creates.All(create => create.TransferAccountId is null));
    }

    [TestMethod]
    public void ASecondCopyOfAMovementIsRemovedAndCanBeLeftAlone()
    {
        var kept = Sample.T("t1", "2026-10-23", -19.71m, "PayPal");
        var copy = Sample.T("t2", "2026-10-23", -19.71m, "PayPal");
        var reconciliation = ReconciliationWith(
            [AccountReconciliationOf(Giro, duplicate: new DuplicateOf(copy, kept, 0, 1.0))]
        );

        Assert.AreEqual(1, RepairPlanBuilder.From(reconciliation, []).Deletes.Count);
        Assert.AreEqual(0, RepairPlanBuilder.From(reconciliation, [], deleteDuplicates: false).Deletes.Count);
    }

    private static YnabAccount Account(string id, string name) =>
        new(id, name, "checking", OnBudget: true, Closed: false, Money.Zero, Money.Zero, Money.Zero);

    private static BankRow Row(int index, string day, decimal amount, string payee) =>
        new(DateOnly.Parse(day), amount, payee, "memo", "export.csv", index);

    private static AccountReconciliation AccountReconciliationOf(
        YnabAccount account,
        BankRow? missing = null,
        DuplicateOf? duplicate = null
    ) =>
        new(
            Account: account,
            Statement: new BankStatement("export.csv", account.Name, ExportStatement.Unknown, []),
            Cutoff: new DateOnly(2026, 10, 25),
            ExportBalance: null,
            PlanBalanceAtCutoff: Money.Zero,
            Matches: [],
            MissingInPlan: missing is null ? [] : [missing],
            OnlyInPlan: [],
            Duplicates: duplicate is null ? [] : [duplicate],
            Adjustments: [],
            ZeroAmountRows: [],
            TransferTargets: new Dictionary<int, string>(),
            Gap: null
        );

    private static Reconciliation ReconciliationWith(
        IReadOnlyList<AccountReconciliation> accounts,
        IReadOnlyList<Transfer>? transfers = null
    ) => new(Snapshot: Sample.Snapshot(), Accounts: accounts, Unresolved: [], Transfers: transfers ?? []);
}

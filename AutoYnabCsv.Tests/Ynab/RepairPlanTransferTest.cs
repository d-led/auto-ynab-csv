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

    [TestMethod]
    public void ATransferPointingAtTheWrongAccountIsReplacedNotKept()
    {
        var wrongOut = Sample.T("w1", "2025-10-29", -500m, "Transfer : GiroTogether", transferAccountId: Together.Id);
        var wrongIn = Sample.T("w2", "2025-10-29", 500m, "Transfer : DKB Giro", transferAccountId: Giro.Id);
        var realOut = Row(10, "2025-10-29", -500m, "Dmitry");
        var realIn = Row(11, "2025-10-29", 500m, "Dmitry");
        var n26 = Account("acc-n26", "n26");
        var real = new Transfer(realOut, realIn, "DKB Giro", "n26", 0, ConfirmedByAccountNumber: true);

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(
                [AccountReconciliationOf(Giro, missing: realOut, extra: wrongOut), AccountReconciliationOf(n26, missing: realIn, extra: wrongIn)],
                [real],
                [new TransferRepoint(new PlanTransferPair(wrongOut, wrongIn, "DKB Giro", "GiroTogether"), real)]
            ),
            []
        );

        Assert.AreEqual(1, plan.Creates.Count, "the real transfer, once");
        Assert.AreEqual("acc-n26", plan.Creates[0].TransferAccountId);
        Assert.AreEqual(1, plan.Deletes.Count, "and the pair the plan invented goes with one delete");
        Assert.AreEqual("w1", plan.Deletes[0].Transaction.Id);
    }

    [TestMethod]
    public void ARowTheBankHasInAnotherAccountIsMovedNotRecreated()
    {
        var inTheWrongAccount = Sample.T("x1", "2025-11-24", -500m, "Transfer : DKB Giro", transferAccountId: Giro.Id);
        var asTheBankHasIt = Row(20, "2025-11-24", -500m, "Dmitry");
        var move = new Reaccounting(inTheWrongAccount, "GiroTogether", "DKB Giro", asTheBankHasIt);

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(
                [
                    AccountReconciliationOf(Giro, missing: asTheBankHasIt),
                    AccountReconciliationOf(Together, extra: inTheWrongAccount),
                ],
                reaccountings: [move]
            ),
            []
        );

        Assert.AreEqual(1, plan.Moves.Count);
        Assert.AreEqual(0, plan.Creates.Count, "the row is not created a second time");
        Assert.AreEqual(0, plan.Deletes.Count, "and not deleted from where it wrongly sits");
        Assert.AreEqual("acc-giro", plan.Moves[0].ToAccountId);
        Assert.AreEqual("acc-together", plan.Moves[0].FromAccountId);
    }

    [TestMethod]
    public void APlanWithNothingToDoSaysSo()
    {
        var nothing = new RepairPlan("plan-1", DateTimeOffset.UnixEpoch, [], [], [], [], []);
        var something = new RepairPlan(
            "plan-1",
            DateTimeOffset.UnixEpoch,
            [new PlannedCreate("acc-giro", "DKB Giro", new DateOnly(2026, 1, 14), -218m, "Dmitry", "memo", "ayc-x", null)],
            [],
            [],
            [],
            []
        );

        Assert.IsTrue(nothing.IsEmpty);
        Assert.IsFalse(something.IsEmpty, "one row to add is not nothing");
        Assert.AreEqual(1, something.Total);
    }

    private static YnabAccount Account(string id, string name) =>
        new(id, name, "checking", OnBudget: true, Closed: false, Money.Zero, Money.Zero, Money.Zero);

    private static BankRow Row(int index, string day, decimal amount, string payee) =>
        new(DateOnly.Parse(day), amount, payee, "memo", "export.csv", index);

    private static AccountReconciliation AccountReconciliationOf(
        YnabAccount account,
        BankRow? missing = null,
        DuplicateOf? duplicate = null,
        YnabTransaction? extra = null
    ) =>
        new(
            Account: account,
            Statement: new BankStatement("export.csv", account.Name, ExportStatement.Unknown, []),
            Cutoff: new DateOnly(2026, 10, 25),
            ExportBalance: null,
            PlanBalanceAtCutoff: Money.Zero,
            Matches: [],
            MissingInPlan: missing is null ? [] : [missing],
            OnlyInPlan: extra is null ? [] : [extra],
            Duplicates: duplicate is null ? [] : [duplicate],
            Adjustments: [],
            ZeroAmountRows: [],
            TransferTargets: new Dictionary<int, string>(),
            Gap: null
        );

    private static Reconciliation ReconciliationWith(
        IReadOnlyList<AccountReconciliation> accounts,
        IReadOnlyList<Transfer>? transfers = null,
        IReadOnlyList<TransferRepoint>? repoints = null,
        IReadOnlyList<Reaccounting>? reaccountings = null
    ) =>
        new(
            Snapshot: Sample.Snapshot(),
            Accounts: accounts,
            Unresolved: [],
            Transfers: transfers ?? [],
            Repoints: repoints ?? [],
            Reaccountings: reaccountings ?? []
        );
}

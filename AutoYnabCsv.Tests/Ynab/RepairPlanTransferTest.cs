using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class RepairPlanTransferTest
{
    private static readonly YnabAccount Giro = Account("acc-giro", "DKB Giro");

    private static readonly YnabAccount Together = Account("acc-together", "Shared");

    [TestMethod]
    public void AMovementBetweenTwoAccountsIsAddedOnceAsATransfer()
    {
        var outgoing = Row(0, "2026-01-14", -218m, "Girokonto · 0002");
        var incoming = Row(1, "2026-01-14", 218m, "Girokonto · 0003");

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(
                [AccountReconciliationOf(Giro, outgoing), AccountReconciliationOf(Together, incoming)],
                [new Transfer(outgoing, incoming, "Girokonto · 0002", "Girokonto · 0003", 0, ConfirmedByAccountNumber: true)]
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
        var wrongOut = Sample.T("w1", "2025-10-29", -500m, "Transfer : Shared", transferAccountId: Together.Id);
        var wrongIn = Sample.T("w2", "2025-10-29", 500m, "Transfer : DKB Giro", transferAccountId: Giro.Id);
        var realOut = Row(10, "2025-10-29", -500m, "Some Person");
        var realIn = Row(11, "2025-10-29", 500m, "Some Person");
        var n26 = Account("acc-n26", "n26");
        var real = new Transfer(realOut, realIn, "DKB Giro", "n26", 0, ConfirmedByAccountNumber: true);

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(
                [AccountReconciliationOf(Giro, missing: realOut, extras: [wrongOut]), AccountReconciliationOf(n26, missing: realIn, extras: [wrongIn])],
                [real],
                [new TransferRepoint(new PlanTransferPair(wrongOut, wrongIn, "DKB Giro", "Shared"), real)]
            ),
            []
        );

        Assert.AreEqual(1, plan.Creates.Count, "the real transfer, once");
        Assert.AreEqual("acc-n26", plan.Creates[0].TransferAccountId);
        CollectionAssert.AreEquivalent(
            new[] { "w1", "w2" },
            plan.Deletes.Select(delete => delete.Transaction.Id).ToArray(),
            "both halves are asked for; whichever goes first takes the pair with it"
        );
    }

    [TestMethod]
    public void ARowTheBankHasInAnotherAccountIsMovedNotRecreated()
    {
        var inTheWrongAccount = Sample.T("x1", "2025-11-24", -500m, "Transfer : DKB Giro", transferAccountId: Giro.Id);
        var asTheBankHasIt = Row(20, "2025-11-24", -500m, "Some Person");
        var move = new Reaccounting(inTheWrongAccount, "Shared", "DKB Giro", asTheBankHasIt);

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(
                [
                    AccountReconciliationOf(Giro, missing: asTheBankHasIt),
                    AccountReconciliationOf(Together, extras: [inTheWrongAccount]),
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
            [new PlannedCreate("acc-giro", "DKB Giro", new DateOnly(2026, 1, 14), -218m, "Some Person", "memo", "ayc-x", null)],
            [],
            [],
            [],
            []
        );

        Assert.IsTrue(nothing.IsEmpty);
        Assert.IsFalse(something.IsEmpty, "one row to add is not nothing");
        Assert.AreEqual(1, something.Total);
    }

    [TestMethod]
    public void DeletingAHalfOfATransferBringsBackTheHalfTheExportHas()
    {
        var bogus = Sample.T("bad", "2026-04-01", -1.12m, "Transfer : Shared", transferAccountId: Together.Id);
        var wanted = Sample.T("good", "2026-04-01", 1.12m, "Transfer : DKB Giro") with
        {
            AccountId = Together.Id,
            AccountName = Together.Name,
            TransferAccountId = Giro.Id,
        };

        var reconciliation = new Reconciliation(
            Snapshot: SnapshotOf(bogus, wanted),
            Accounts: [AccountReconciliationOf(Giro, extras: [bogus]), AccountReconciliationOf(Together)],
            Unresolved: [],
            Transfers: [],
            Repoints: [],
            Reaccountings: []
        );

        var plan = RepairPlanBuilder.From(reconciliation, []);

        Assert.AreEqual(1, plan.Deletes.Count, "the half the bank does not have goes");
        var rescue = plan.Creates.SingleOrDefault(create => create.Amount == 1.12m);
        Assert.IsNotNull(rescue, "and the half the bank does have is written again, or the delete takes it too");
        Assert.AreEqual(Together.Id, rescue.AccountId);
        Assert.AreEqual("DKB Giro", rescue.PayeeName, "a row that is no longer a transfer cannot be paid to one");
    }

    [TestMethod]
    public void APairTheExportsDoNotShowIsRemovedAndNotWrittenBack()
    {
        var outLeg = Sample.T("o1", "2024-05-30", -188.48m, "Transfer : DKB Giro", transferAccountId: Together.Id);
        var inLeg = outLeg with
        {
            Id = "i1",
            Amount = 188.48m,
            AccountId = Together.Id,
            AccountName = Together.Name,
            TransferAccountId = Giro.Id,
        };

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(
                [AccountReconciliationOf(Giro, extras: [outLeg]), AccountReconciliationOf(Together, extras: [inLeg])],
                snapshot: SnapshotOf(outLeg, inLeg)
            ),
            []
        );

        CollectionAssert.AreEquivalent(
            new[] { "o1", "i1" },
            plan.Deletes.Select(delete => delete.Transaction.Id).ToArray(),
            "both halves go"
        );
        Assert.AreEqual(
            0,
            plan.Creates.Count,
            "and nothing is written back, because the exports do not have this movement either"
        );
    }

    [TestMethod]
    public void ADuplicatedLegIsAskedForTwiceAndTheHalfTheBankHasComesBack()
    {
        var realHalf = Sample.T("real", "2025-10-29", 500m, "Transfer : Shared", transferAccountId: Together.Id);
        var firstLeg = Sample.T("leg1", "2025-10-29", -500m, "Transfer : DKB Giro", transferAccountId: Giro.Id);
        var secondLeg = firstLeg with { Id = "leg2" };

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(
                [
                    AccountReconciliationOf(Giro),
                    AccountReconciliationOf(Together, extras: [firstLeg, secondLeg]),
                ],
                snapshot: SnapshotOf(realHalf, firstLeg, secondLeg)
            ),
            []
        );

        CollectionAssert.AreEquivalent(
            new[] { "leg1", "leg2" },
            plan.Deletes.Select(delete => delete.Transaction.Id).ToArray(),
            "a pair with one leg too many needs the surplus leg asked for by name"
        );
        var rescue = plan.Creates.Single();
        Assert.AreEqual(Giro.Id, rescue.AccountId);
        Assert.AreEqual(500m, rescue.Amount);
        Assert.AreEqual("Shared", rescue.PayeeName, "written back as an ordinary row, once");
    }

    private static YnabSnapshot SnapshotOf(params YnabTransaction[] transactions) =>
        new(
            PlanId: "plan-1",
            PlanName: "Household",
            CurrencyFormat: "EUR",
            FetchedAt: DateTimeOffset.UnixEpoch,
            Accounts: [Giro, Together],
            Categories: [],
            Payees: [],
            Transactions: transactions
        );

    private static YnabAccount Account(string id, string name) =>
        new(id, name, "checking", OnBudget: true, Closed: false, Money.Zero, Money.Zero, Money.Zero);

    private static BankRow Row(int index, string day, decimal amount, string payee) =>
        new(DateOnly.Parse(day), amount, payee, "memo", "export.csv", index);

    private static AccountReconciliation AccountReconciliationOf(
        YnabAccount account,
        BankRow? missing = null,
        DuplicateOf? duplicate = null,
        IReadOnlyList<YnabTransaction>? extras = null
    ) =>
        new(
            Account: account,
            Statement: new BankStatement("export.csv", account.Name, ExportStatement.Unknown, []),
            Cutoff: new DateOnly(2026, 10, 25),
            ExportBalance: null,
            PlanBalanceAtCutoff: Money.Zero,
            Matches: [],
            MissingInPlan: missing is null ? [] : [missing],
            OnlyInPlan: extras ?? [],
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
        IReadOnlyList<Reaccounting>? reaccountings = null,
        YnabSnapshot? snapshot = null
    ) =>
        new(
            Snapshot: snapshot ?? Sample.Snapshot(),
            Accounts: accounts,
            Unresolved: [],
            Transfers: transfers ?? [],
            Repoints: repoints ?? [],
            Reaccountings: reaccountings ?? []
        );
}

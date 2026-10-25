using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class RepairPlanAdjustmentTest
{
    [TestMethod]
    public void AnAdjustmentThePlanHoldsIsLeftAlone()
    {
        var adjustment = Sample.T("a1", "2026-10-25", 741.55m, "Balance adjustment");

        var plan = RepairPlanBuilder.From(
            ReconciliationWith(Giro, adjustment),
            []
        );

        Assert.AreEqual(
            0,
            plan.Deletes.Count,
            "removing it would put the gap back and the next reconcile would ask for the same adjustment again"
        );
        Assert.AreEqual(0, plan.Creates.Count, "and nothing is written to replace it");
    }

    [TestMethod]
    public void SeveralAdjustmentsAreWrittenAgainAsOneRowWithTheirSum()
    {
        var first = Sample.T("a1", "2026-09-01", 400m, "Balance adjustment");
        var second = Sample.T("a2", "2026-10-25", -150m, "Balance adjustment");

        var plan = RepairPlanBuilder.From(ReconciliationWith(Giro, first, second), []);

        CollectionAssert.AreEquivalent(
            new[] { "a1", "a2" },
            plan.Deletes.Select(delete => delete.Transaction.Id).ToArray(),
            "the pile goes"
        );
        var one = plan.Creates.Single();
        Assert.AreEqual(250m, one.Amount);
        Assert.AreEqual(new DateOnly(2026, 10, 25), one.Date, "written where the last of them was");
        Assert.AreEqual("Balance adjustment", one.PayeeName);
    }

    [TestMethod]
    public void AdjustmentsThatCancelOutAreJustRemoved()
    {
        var plus = Sample.T("a1", "2026-09-01", 400m, "Balance adjustment");
        var minus = plus with { Id = "a2", Amount = -400m };

        var plan = RepairPlanBuilder.From(ReconciliationWith(Giro, plus, minus), []);

        Assert.AreEqual(2, plan.Deletes.Count);
        Assert.AreEqual(0, plan.Creates.Count, "nothing left to say");
    }

    [TestMethod]
    public void OneAdjustmentOnItsOwnIsLeftWhereItIs()
    {
        var plan = RepairPlanBuilder.From(ReconciliationWith(Giro, Sample.T("a1", "2026-10-25", 250m, "Balance adjustment")), []);

        Assert.AreEqual(0, plan.Deletes.Count);
        Assert.AreEqual(0, plan.Creates.Count);
    }

    [TestMethod]
    public void ARowThatSaysNothingAboutAdjustingIsStillRemoved()
    {
        var ordinary = Sample.T("p1", "2026-10-25", -19.71m, "PayPal");

        var plan = RepairPlanBuilder.From(ReconciliationWith(Giro, ordinary), []);

        Assert.AreEqual(1, plan.Deletes.Count);
        Assert.AreEqual("p1", plan.Deletes[0].Transaction.Id);
    }

    [TestMethod]
    public void NothingIsRemovedWhenExtrasAreToBeKept()
    {
        var plan = RepairPlanBuilder.From(ReconciliationWith(Giro, Sample.T("p1", "2026-10-25", -19.71m, "PayPal")), [], deleteExtras: false);

        Assert.AreEqual(0, plan.Deletes.Count, "keeping the extras means keeping them, adjustments or not");
    }

    private static Reconciliation ReconciliationWith(YnabAccount account, params YnabTransaction[] onlyInPlan) =>
        new(
            Snapshot: new YnabSnapshot(
                PlanId: "plan-1",
                PlanName: "Household",
                CurrencyFormat: "EUR",
                FetchedAt: DateTimeOffset.UnixEpoch,
                Accounts: [account],
                Categories: [],
                Payees: [],
                Transactions: onlyInPlan
            ),
            Accounts:
            [
                new AccountReconciliation(
                    Account: account,
                    Statement: new BankStatement("export.csv", account.Name, ExportStatement.Unknown, []),                    Cutoff: new DateOnly(2026, 10, 25),
                    ExportBalance: Money.Zero,
                    PlanBalanceAtCutoff: Money.Zero,
                    Matches: [],
                    MissingInPlan: [],
                    OnlyInPlan: onlyInPlan,
                    Duplicates: [],
                    Adjustments: [],
                    ZeroAmountRows: [],
                    TransferTargets: new Dictionary<int, string>(),
                    Gap: null
                ),
            ],
            Unresolved: [],
            Transfers: [],
            Repoints: [],
            Reaccountings: []
        );

    private static readonly YnabAccount Giro =
        new("acc-giro", "Girokonto", "checking", OnBudget: true, Closed: false, Money.Zero, Money.Zero, Money.Zero);
}

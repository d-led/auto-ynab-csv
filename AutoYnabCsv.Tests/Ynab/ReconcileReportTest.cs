using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Categorize;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;
using AutoYnabCsv.Ynab.Report;

namespace Tests.Ynab;

[TestClass]
public class ReconcileReportTest
{
    [TestMethod]
    public void TheDecisionListStopsAtFiftyAndSaysHowManyAreLeft()
    {
        var snapshot = Sample.Snapshot([.. SixtyPayeesFiledTwoWays()]);

        var markdown = ReconcileReport.Markdown(ReconciliationOf(snapshot), CategoryProposals.For(snapshot));

        Assert.AreEqual(50, DecisionRows(markdown), "nobody is offered more than 50 transactions to review");
        StringAssert.Contains(markdown, "… and 10 more");
    }

    [TestMethod]
    public void AnAccountThatAgreesIsNotListedAsOff()
    {
        var snapshot = Sample.Snapshot();

        var markdown = ReconcileReport.Markdown(ReconciliationOf(snapshot, exportBalance: 0m, planBalance: 0m), []);

        StringAssert.Contains(markdown, "0 account(s) do not hold what the export says");
    }

    private static int DecisionRows(string markdown) =>
        markdown.Split('\n').Count(line => line.StartsWith("| 2026-10-23", StringComparison.Ordinal));

    private static IEnumerable<YnabTransaction> SixtyPayeesFiledTwoWays()
    {
        for (var index = 0; index < 60; index++)
        {
            var payee = $"Shop {index}";
            yield return Sample.T($"a{index}", "2026-08-01", -10m - index, payee, Sample.Groceries);
            yield return Sample.T($"b{index}", "2026-09-01", -11m - index, payee, Sample.Dining);
            yield return Sample.T($"n{index}", "2026-10-23", -12m - index, payee);
        }
    }

    private static Reconciliation ReconciliationOf(YnabSnapshot snapshot, decimal exportBalance = 0m, decimal planBalance = 0m)
    {
        var statement = new BankStatement(
            SourceFile: "export.csv",
            Label: "Girokonto · 0001",
            Statement: new ExportStatement("Girokonto", "DE00000000000000000001", null, null, new DateOnly(2026, 10, 25), exportBalance),
            Rows: []
        );

        return new Reconciliation(
            Snapshot: snapshot,
            Accounts:
            [
                new AccountReconciliation(
                    Account: Sample.Giro,
                    Statement: statement,
                    Cutoff: new DateOnly(2026, 10, 25),
                    ExportBalance: exportBalance,
                    PlanBalanceAtCutoff: planBalance,
                    Matches: [],
                    MissingInPlan: [],
                    OnlyInPlan: [],
                    Duplicates: [],
                    Adjustments: [],
                    ZeroAmountRows: [],
                    TransferTargets: new Dictionary<int, string>(),
                    Gap: null
                )
            ],
            Unresolved: [],
            Transfers: [],
            Repoints: [],
            Reaccountings: []
        );
    }
}

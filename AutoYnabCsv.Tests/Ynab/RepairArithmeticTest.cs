using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class RepairArithmeticTest
{
    [TestMethod]
    public void AMissingRowIsAddedToTheProjection()
    {
        var account = AccountWith(exportBalance: 100m, planBalance: 120m, missing: Row(-20m));

        Assert.AreEqual(-20m, account.RepairDelta);
        Assert.AreEqual(0m, account.DifferenceAfterRepair, "adding the outflow the plan never saw brings it back to the bank");
    }

    [TestMethod]
    public void AMissingTransferIsAddedOnceEvenThoughItHasTwoHalves()
    {
        var account = AccountWith(exportBalance: 100m, planBalance: 120m, missing: Row(-20m));
        var opposite = AccountWith(exportBalance: 100m, planBalance: 80m, missing: Row(20m));

        Assert.AreEqual(-20m, account.RepairDelta);
        Assert.AreEqual(20m, opposite.RepairDelta, "each side is projected on its own account");
    }

    [TestMethod]
    public void RemovingADuplicateRaisesTheBalanceItWasInflating()
    {
        var account = AccountWith(
            exportBalance: 1000m,
            planBalance: 980.29m,
            duplicate: new DuplicateOf(Sample.T("copy", "2026-10-23", -19.71m, "PayPal"), Sample.T("kept", "2026-10-23", -19.71m, "PayPal"), 0, 1.0)
        );

        Assert.AreEqual(19.71m, account.RepairDelta, "the copy is what made the plan lower than the bank");
        Assert.AreEqual(0m, account.DifferenceAfterRepair);
    }

    private static BankRow Row(decimal amount) =>
        new(new DateOnly(2026, 10, 23), amount, "Payee", "memo", "export.csv", 0);

    private static AccountReconciliation AccountWith(
        decimal exportBalance,
        decimal planBalance,
        BankRow? missing = null,
        DuplicateOf? duplicate = null
    ) =>
        new(
            Account: new YnabAccount("acc", "DKB Giro", "checking", true, false, Money.Zero, Money.Zero, Money.Zero),
            Statement: new BankStatement("export.csv", "DKB Giro", ExportStatement.Unknown, []),
            Cutoff: new DateOnly(2026, 10, 25),
            ExportBalance: exportBalance,
            PlanBalanceAtCutoff: planBalance,
            Matches: [],
            MissingInPlan: missing is null ? [] : [missing],
            OnlyInPlan: [],
            Duplicates: duplicate is null ? [] : [duplicate],
            Adjustments: [],
            ZeroAmountRows: [],
            TransferTargets: new Dictionary<int, string>(),
            Gap: null
        );
}

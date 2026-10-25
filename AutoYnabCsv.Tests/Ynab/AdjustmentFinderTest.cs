using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class AdjustmentFinderTest
{
    [TestMethod]
    public void AnEntryCalledBalanceAdjustmentIsACorrection()
    {
        var found = AdjustmentFinder.Find(
            [Sample.T("t1", "2026-09-01", -250m, "Balance Adjustment")],
            Sample.Snapshot()
        );

        Assert.AreEqual(1, found.Count);
        StringAssert.Contains(found[0].Why, "correction");
    }

    [TestMethod]
    public void AnOrdinaryPurchaseIsNotACorrection()
    {
        var found = AdjustmentFinder.Find(
            [Sample.T("t1", "2026-09-01", -26.74m, "Harbour Guitars")],
            Sample.Snapshot()
        );

        Assert.AreEqual(0, found.Count);
    }

    [TestMethod]
    public void ARoundAmountNobodyExplainsIsStillNamedAsASuspect()
    {
        var found = AdjustmentFinder.Find(
            [Sample.T("t1", "2026-09-01", -250m, "Unknown shop")],
            Sample.Snapshot()
        );

        Assert.AreEqual(1, found.Count);
        StringAssert.Contains(found[0].Why, "round amount");
    }
}

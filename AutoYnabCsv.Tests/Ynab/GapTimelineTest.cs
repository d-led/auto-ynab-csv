using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class GapTimelineTest
{
    [TestMethod]
    public void TwoLedgersThatAgreeEveryDayShowNoGap()
    {
        var gap = GapTimeline.Of(
            [MatcherTest.Row(0, "2026-05-01", -10m)],
            [MatcherTest.Plan("t1", "2026-05-01", -10m, "Shop")],
            cutoff: new DateOnly(2026, 5, 10),
            exportBalance: 100m,
            planBalanceAtCutoff: 100m
        );

        Assert.IsTrue(gap.LedgersAgree);
        Assert.AreEqual(0m, gap.WorstDifference);
    }

    [TestMethod]
    public void TheGapStartsOnTheDayTheRowIsMissingFromThePlan()
    {
        var gap = GapTimeline.Of(
            [MatcherTest.Row(0, "2026-05-01", -10m), MatcherTest.Row(1, "2026-05-06", -20m)],
            [MatcherTest.Plan("t1", "2026-05-01", -10m, "Shop")],
            cutoff: new DateOnly(2026, 5, 10),
            exportBalance: 100m,
            planBalanceAtCutoff: 120m
        );

        Assert.AreEqual(new DateOnly(2026, 5, 6), gap.DivergesFrom, "the missing 20 left the plan on 6 May");
        Assert.AreEqual(new DateOnly(2026, 5, 10), gap.DivergesTo);
        Assert.AreEqual(20m, gap.WorstDifference);
    }

    [TestMethod]
    public void EachDayOnTheTimelineHoldsWhatAStatementWouldShowAtItsEnd()
    {
        var gap = GapTimeline.Of(
            [MatcherTest.Row(0, "2026-05-01", -10m), MatcherTest.Row(1, "2026-05-06", -20m)],
            [MatcherTest.Plan("t1", "2026-05-01", -10m, "Shop"), MatcherTest.Plan("t2", "2026-05-06", -20m, "Shop")],
            cutoff: new DateOnly(2026, 5, 10),
            exportBalance: 100m,
            planBalanceAtCutoff: 100m
        );

        Assert.AreEqual(new DateOnly(2026, 5, 1), gap.Points[0].Day);
        Assert.AreEqual(120m, gap.Points[0].Bank, "after the 10 left on 1 May, and before the 20 left on 6 May");
        Assert.AreEqual(100m, gap.Points[^1].Bank);
        Assert.AreEqual(new DateOnly(2026, 5, 10), gap.Points[^1].Day);
    }
}

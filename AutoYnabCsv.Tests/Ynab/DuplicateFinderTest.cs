using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class DuplicateFinderTest
{
    [TestMethod]
    public void ASecondCopyOfAMatchedMovementIsADuplicateAndNotMissingMoney()
    {
        var kept = MatcherTest.Plan("t1", "2026-10-23", -19.71m, "PayPal Europe");
        var extra = MatcherTest.Plan("t2", "2026-10-23", -19.71m, "PayPal Europe");

        var duplicates = DuplicateFinder.Find([new Match(MatcherTest.Row(0, "2026-10-23", -19.71m, "PayPal"), kept)], [extra]);

        Assert.AreEqual(1, duplicates.Count);
        Assert.AreEqual("t2", duplicates[0].Extra.Id);
        Assert.AreEqual(1.0, duplicates[0].Similarity, "the two descriptions are the same words");
    }

    [TestMethod]
    public void ADifferentAmountIsNotADuplicate()
    {
        var kept = MatcherTest.Plan("t1", "2026-10-23", -19.71m, "PayPal");
        var other = MatcherTest.Plan("t2", "2026-10-23", -19.72m, "PayPal");

        var duplicates = DuplicateFinder.Find([new Match(MatcherTest.Row(0, "2026-10-23", -19.71m, "PayPal"), kept)], [other]);

        Assert.AreEqual(0, duplicates.Count);
    }

    [TestMethod]
    public void TheSameAmountForADifferentShopIsNotADuplicate()
    {
        var kept = MatcherTest.Plan("t1", "2026-10-23", -19.71m, "PayPal");
        var other = MatcherTest.Plan("t2", "2026-10-23", -19.71m, "Kaufland");

        var duplicates = DuplicateFinder.Find([new Match(MatcherTest.Row(0, "2026-10-23", -19.71m, "PayPal"), kept)], [other]);

        Assert.AreEqual(0, duplicates.Count);
    }
}

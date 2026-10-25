using AutoYnabCsv.Ynab.Categorize;

namespace Tests.Ynab;

[TestClass]
public class CategoryProposalsTest
{
    [TestMethod]
    public void APayeeFiledTheSameWayThreeTimesGetsThatCategory()
    {
        var proposals = CategoryProposals.For(
            Sample.Snapshot(
                Sample.T("h1", "2026-08-01", -20m, "EDEKA", Sample.Groceries),
                Sample.T("h2", "2026-09-01", -30m, "EDEKA", Sample.Groceries),
                Sample.T("h3", "2026-10-01", -40m, "EDEKA", Sample.Groceries),
                Sample.T("new", "2026-10-23", -6.82m, "EDEKA")
            )
        );

        Assert.AreEqual(1, proposals.Count);
        Assert.IsTrue(proposals[0].IsConfident, "3 of 3 is a habit");
        Assert.AreEqual("Groceries", proposals[0].Category.Name);
    }

    [TestMethod]
    public void APayeeFiledTwoWaysIsLeftForAPerson()
    {
        var proposals = CategoryProposals.For(
            Sample.Snapshot(
                Sample.T("h1", "2026-08-01", -20m, "REWE", Sample.Groceries),
                Sample.T("h2", "2026-09-01", -30m, "REWE", Sample.Dining),
                Sample.T("new", "2026-10-23", -6.82m, "REWE")
            )
        );

        Assert.AreEqual(1, proposals.Count);
        Assert.IsFalse(proposals[0].IsConfident, "half and half is a coin toss");
    }

    [TestMethod]
    public void ATransferIsNeverGivenACategory()
    {
        var proposals = CategoryProposals.For(
            Sample.Snapshot(
                Sample.T("h1", "2026-10-01", -200m, "Transfer : Sparkonto", transferAccountId: "acc-savings"),
                Sample.T("h2", "2026-10-02", -100m, "Transfer : Sparkonto", transferAccountId: "acc-savings"),
                Sample.T("h3", "2026-10-03", -100m, "Transfer : Sparkonto", transferAccountId: "acc-savings"),
                Sample.T("new", "2026-10-23", -100m, "Transfer : Sparkonto", transferAccountId: "acc-savings")
            )
        );

        Assert.AreEqual(0, proposals.Count, "YNAB refuses a category on a transfer");
    }

    [TestMethod]
    public void IncomeIsFiledByYnabItself()
    {
        var proposals = CategoryProposals.For(
            Sample.Snapshot(
                Sample.T("h1", "2026-10-01", 2000m, "Employer", Sample.ReadyToAssign),
                Sample.T("h2", "2026-09-01", 2000m, "Employer", Sample.ReadyToAssign),
                Sample.T("h3", "2026-08-01", 2000m, "Employer", Sample.ReadyToAssign),
                Sample.T("new", "2026-10-23", 2000m, "Employer")
            )
        );

        Assert.AreEqual(0, proposals.Count, "an inflow is income, not spending to be filed");
    }
}

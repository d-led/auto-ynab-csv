using AutoYnabCsv.Ynab.Contracts;

namespace Tests.Ynab;

[TestClass]
public class SnapshotTest
{
    [TestMethod]
    public void TheBalanceOnADayLeavesOutWhatWasBookedAfterIt()
    {
        var snapshot = Snapshot(
            balance: 1000m,
            MatcherTest.Plan("t1", "2026-10-20", -500m, "One"),
            MatcherTest.Plan("t2", "2026-10-25", -300m, "Two")
        );

        Assert.AreEqual(1300m, snapshot.BalanceAt("acc", new DateOnly(2026, 10, 21)));
    }

    [TestMethod]
    public void TheBalanceOnTheLastDayIsWhatThePlanHoldsNow()
    {
        var snapshot = Snapshot(balance: 1000m, MatcherTest.Plan("t1", "2026-10-20", -500m, "One"));

        Assert.AreEqual(1000m, snapshot.BalanceAt("acc", new DateOnly(2026, 10, 25)));
    }

    private static YnabSnapshot Snapshot(decimal balance, params YnabTransaction[] transactions) =>
        new(
            PlanId: "plan-1",
            PlanName: "Household",
            CurrencyFormat: "EUR",
            FetchedAt: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
            Accounts: [new YnabAccount("acc", "Girokonto", "checking", true, false, balance, Money.Zero, Money.Zero)],
            Categories: [],
            Payees: [],
            Transactions: transactions
        );
}

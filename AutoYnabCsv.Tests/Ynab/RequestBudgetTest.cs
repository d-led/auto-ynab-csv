using AutoYnabCsv.Ynab.Http;

namespace Tests.Ynab;

[TestClass]
public class RequestBudgetTest
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>As long as a command is willing to sit still for one request.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [TestMethod]
    public async Task TwoHundredRequestsInAnHourAreNotMadeToWait()
    {
        var budget = new RequestBudget(perHour: 200, clock: () => Noon);

        for (var request = 0; request < 200; request++)
        {
            await budget.AcquireAsync(Patience);
        }

        Assert.AreEqual(0, budget.Remaining);
        Assert.AreEqual(TimeSpan.FromHours(1), budget.DelayBeforeNext(), "the 201st waits for the first to age out");
    }

    [TestMethod]
    public async Task TheWaitIsOnlyUntilTheOldestRequestLeavesTheWindow()
    {
        var now = Noon;
        var budget = new RequestBudget(perHour: 2, clock: () => now);

        await budget.AcquireAsync(Patience);
        now = Noon.AddMinutes(50);
        await budget.AcquireAsync(Patience);

        Assert.AreEqual(TimeSpan.FromMinutes(10), budget.DelayBeforeNext(), "the window rolls, it does not reset");
    }

    [TestMethod]
    public async Task AFullWindowIsWaitedOutBeforeTheRequestIsMade()
    {
        var now = Noon;
        var waited = new List<TimeSpan>();
        var budget = new RequestBudget(
            perHour: 1,
            clock: () => now,
            wait: (delay, _) =>
            {
                waited.Add(delay);
                now = now.Add(delay);
                return Task.CompletedTask;
            }
        );

        await budget.AcquireAsync(TimeSpan.FromHours(1));
        await budget.AcquireAsync(TimeSpan.FromHours(1));

        Assert.AreEqual(TimeSpan.FromHours(1), waited.Single());
        Assert.AreEqual(2, budget.Spent);
    }

    [TestMethod]
    public async Task AWaitLongerThanTheCommandWillSitThroughIsRefusedWithTheTime()
    {
        var budget = new RequestBudget(perHour: 1, clock: () => Noon);
        await budget.AcquireAsync(TimeSpan.Zero);

        var failure = await Assert.ThrowsExactlyAsync<RequestBudgetExhaustedException>(() =>
            budget.AcquireAsync(TimeSpan.FromSeconds(60))
        );

        Assert.AreEqual(TimeSpan.FromHours(1), failure.Until);
        StringAssert.Contains(failure.Message, "60 minute", "the operator is told when to come back");
        StringAssert.Contains(failure.Message, "Nothing was sent", "nothing was spent on a budget that is not there");
    }

    [TestMethod]
    public void AnHourAnotherRunSpentIsThisRunsHourToo()
    {
        var ledger = Path.Combine(Path.GetTempPath(), $"ayc-budget-{Guid.NewGuid():N}.txt");

        SpendTwoHundred(new RequestBudget(perHour: 200, ledgerPath: ledger, clock: () => Noon));

        var nextRun = new RequestBudget(perHour: 200, ledgerPath: ledger, clock: () => Noon.AddMinutes(5));

        Assert.AreEqual(0, nextRun.Remaining, "the file is the memory both runs share");
    }

    [TestMethod]
    public void AnHourAgoIsNobodyS()
    {
        var ledger = Path.Combine(Path.GetTempPath(), $"ayc-budget-{Guid.NewGuid():N}.txt");

        SpendTwoHundred(new RequestBudget(perHour: 200, ledgerPath: ledger, clock: () => Noon));

        var later = new RequestBudget(perHour: 200, ledgerPath: ledger, clock: () => Noon.AddMinutes(61));

        Assert.AreEqual(200, later.Remaining);
    }

    private static void SpendTwoHundred(RequestBudget budget)
    {
        for (var request = 0; request < 200; request++)
        {
            budget.AcquireAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        }
    }
}

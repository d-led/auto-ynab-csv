using AutoYnabCsv.Ynab.Http;

namespace Tests.Ynab;

[TestClass]
public class RequestBudgetTest
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>As long as a command is willing to sit still for one request.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [TestMethod]
    public async Task TwoHundredRequestsTakeTheHourTheyHaveInsteadOfBurstingIt()
    {
        var clock = new TestClock();
        var budget = clock.Budget();

        for (var request = 0; request < 200; request++)
        {
            await budget.AcquireAsync(Patience);
        }

        Assert.AreEqual(0, budget.Remaining);
        Assert.IsTrue(clock.Now - Noon <= TimeSpan.FromHours(1), "the hour is never overrun");
        Assert.IsTrue(
            clock.Waited.All(waited => waited >= RequestBudget.DefaultInterval),
            "no two requests are closer together than the floor"
        );
        Assert.IsTrue(
            clock.Waited.Any(waited => waited > RequestBudget.DefaultInterval),
            "and the last of the hour is spread, not dumped in one breath"
        );
    }

    [TestMethod]
    public async Task TwoRequestsAreNeverBackToBack()
    {
        var clock = new TestClock();
        var budget = clock.Budget();

        await budget.AcquireAsync(Patience);

        Assert.AreEqual(RequestBudget.DefaultInterval, budget.DelayBeforeNext());
    }

    [TestMethod]
    public async Task TheWaitIsOnlyUntilTheOldestRequestLeavesTheWindow()
    {
        var clock = new TestClock();
        var budget = clock.Budget(perHour: 2);

        await budget.AcquireAsync(Patience);
        clock.Now = Noon.AddMinutes(50);
        await budget.AcquireAsync(Patience);

        Assert.AreEqual(TimeSpan.FromMinutes(10), budget.DelayBeforeNext(), "the window rolls, it does not reset");
    }

    [TestMethod]
    public async Task AFullWindowIsWaitedOutBeforeTheRequestIsMade()
    {
        var clock = new TestClock();
        var budget = clock.Budget(perHour: 1);

        await budget.AcquireAsync(TimeSpan.FromHours(1));
        await budget.AcquireAsync(TimeSpan.FromHours(1));

        Assert.AreEqual(TimeSpan.FromHours(1), clock.LongestWait);
        Assert.AreEqual(2, budget.Spent);
    }

    [TestMethod]
    public async Task TheLastOfTheHourIsSpreadRatherThanSpentAtOnce()
    {
        var clock = new TestClock();
        var budget = clock.Budget(perHour: 10);

        for (var request = 0; request < 9; request++)
        {
            await budget.AcquireAsync(Patience);
        }

        clock.Now = Noon.AddMinutes(30);

        Assert.AreEqual(TimeSpan.FromMinutes(15), budget.DelayBeforeNext(), "half of what is left: the last request still has to fit");
    }

    [TestMethod]
    public async Task AWaitLongerThanTheCommandWillSitThroughIsRefusedWithTheTime()
    {
        var clock = new TestClock();
        var budget = clock.Budget(perHour: 1);
        await budget.AcquireAsync(Patience);

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
        var ledger = Ledger();

        SpendTwoHundred(Fixed(ledger, Noon));

        var nextRun = new RequestBudget(perHour: 200, ledgerPath: ledger, clock: () => Noon.AddMinutes(5));

        Assert.AreEqual(0, nextRun.Remaining, "the file is the memory both runs share");
    }

    [TestMethod]
    public void AnHourAgoIsNobodyS()
    {
        var ledger = Ledger();

        SpendTwoHundred(Fixed(ledger, Noon));

        var later = new RequestBudget(perHour: 200, ledgerPath: ledger, clock: () => Noon.AddMinutes(61));

        Assert.AreEqual(200, later.Remaining);
    }

    private static string Ledger() => Path.Combine(Path.GetTempPath(), $"ayc-budget-{Guid.NewGuid():N}.txt");

    /// <summary>A budget whose clock stands still and whose waits therefore cost nothing.</summary>
    private static RequestBudget Fixed(string ledger, DateTimeOffset at) =>
        new(perHour: 200, ledgerPath: ledger, clock: () => at, wait: (_, _) => Task.CompletedTask);

    private static void SpendTwoHundred(RequestBudget budget)
    {
        for (var request = 0; request < 200; request++)
        {
            budget.AcquireAsync(TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        }
    }

    /// <summary>A clock that only moves when something waits, so a test never sleeps.</summary>
    private sealed class TestClock
    {
        public DateTimeOffset Now { get; set; } = Noon;

        public List<TimeSpan> Waited { get; } = [];

        public TimeSpan LongestWait => Waited.Count == 0 ? TimeSpan.Zero : Waited.Max();

        public RequestBudget Budget(int perHour = RequestBudget.PerHour) =>
            new(
                perHour,
                ledgerPath: null,
                clock: () => Now,
                wait: (delay, _) =>
                {
                    Waited.Add(delay);
                    Now = Now.Add(delay);
                    return Task.CompletedTask;
                }
            );
    }
}

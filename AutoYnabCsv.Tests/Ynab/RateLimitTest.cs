using System.Net;
using AutoYnabCsv.Ynab;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Http;

namespace Tests.Ynab;

[TestClass]
public class RateLimitTest
{
    private const string TooManyRequests = """{"error":{"id":"429","name":"too_many_requests","detail":"Too many requests"}}""";

    /// <summary>As long as a command will sit still for one request before giving up on the hour.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [TestMethod]
    public async Task BeingRefusedEndsTheHourRatherThanAskingAgain()
    {
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, TooManyRequests);
        var api = new YnabApi(new HttpClient(handler), "token", new RequestBudget());

        var failure = await Assert.ThrowsExactlyAsync<RequestBudgetExhaustedException>(() =>
            api.CreateAsync("plan-1", Planned())
        );

        Assert.AreEqual(1, handler.Requests.Count, "asking again spends a request that is not there");
        StringAssert.Contains(failure.Message, "This one was refused", "the operator knows it reached the API");
        StringAssert.Contains(failure.Message, "possible in 60 minute", "and when to come back");
    }

    [TestMethod]
    public async Task ARefusalIsBelievedAndRemembered()
    {
        var budget = new RequestBudget();
        var api = new YnabApi(new HttpClient(new StubHandler(HttpStatusCode.TooManyRequests, TooManyRequests)), "token", budget);

        await Assert.ThrowsExactlyAsync<RequestBudgetExhaustedException>(() => api.CreateAsync("plan-1", Planned()));

        Assert.AreEqual(0, budget.Remaining, "the refusal is the only statement the API makes about it");
    }

    [TestMethod]
    public async Task EveryRequestIsCountedOnce()
    {
        var budget = new RequestBudget();
        var api = new YnabApi(new HttpClient(new StubHandler()), "token", budget);

        await api.CreateAsync("plan-1", Planned());
        await api.CreateAsync("plan-1", Planned());

        Assert.AreEqual(2, budget.Spent);
    }

    [TestMethod]
    public async Task AShortWaitIsTakenAndSaidOutLoud()
    {
        var told = new List<string>();
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var budget = new RequestBudget(
            perHour: 1,
            clock: () => now,
            wait: (delay, _) =>
            {
                now = now.Add(delay);
                return Task.CompletedTask;
            }
        );
        var api = new YnabApi(new HttpClient(new StubHandler()), "token", budget, told.Add);

        await api.CreateAsync("plan-1", Planned());
        now = now.AddMinutes(59);
        await api.CreateAsync("plan-1", Planned());

        StringAssert.Contains(told.Single(line => line.Contains("waiting")), "waiting 60s", "a command that sits still says why");
    }

    private static PlannedCreate Planned() =>
        new(
            AccountId: "acc-a",
            AccountName: "DKB Giro",
            Date: new DateOnly(2026, 10, 23),
            Amount: -45.81m,
            PayeeName: "Kaufland",
            Memo: "memo",
            ImportId: "ayc-20261023-abc",
            CategoryId: null
        );
}

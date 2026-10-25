using System.Net;
using System.Text.Json;
using AutoYnabCsv.Ynab;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Http;

namespace Tests.Ynab;

/// <summary>Answers like the API without a network, and keeps what was actually sent.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    internal const string NewId = "t-new";

    private readonly List<Func<HttpResponseMessage>> _answers = [];

    public StubHandler(HttpStatusCode status = HttpStatusCode.OK, string? body = null, (string Name, string Value)[]? headers = null) =>
        Answer(status, body, headers);

    public List<(string Method, string Path, string Body)> Requests { get; } = [];

    /// <summary>Queues the next answer; the last one repeats when the queue runs out.</summary>
    public StubHandler Answer(HttpStatusCode status, string? body = null, (string Name, string Value)[]? headers = null)
    {
        _answers.Add(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? OneTransaction(NewId), System.Text.Encoding.UTF8, "application/json"),
            };

            foreach (var (name, value) in headers ?? [])
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));

        return _answers[Math.Min(Requests.Count - 1, _answers.Count - 1)]();
    }

    /// <summary>The API's own answer, written the way the API writes it.</summary>
    private static string OneTransaction(string id) =>
        JsonSerializer.Serialize(
            new
            {
                data = new
                {
                    transaction = new
                    {
                        id,
                        date = "2026-10-23",
                        amount = -45810,
                        account_id = "acc-a",
                        account_name = "DKB Giro",
                        payee_name = "Kaufland",
                        cleared = "cleared",
                        approved = false,
                    },
                },
            }
        );
}

[TestClass]
public class YnabApiWriteTest
{
    [TestMethod]
    public async Task ACreatedTransactionSaysClearedTheWayTheApiSpellsIt()
    {
        var sent = await Create(Planned("memo"));

        Assert.AreEqual("cleared", sent.GetProperty("cleared").GetString(), "a boolean is rejected; the API wants the word");
        Assert.IsFalse(sent.GetProperty("approved").GetBoolean(), "nothing lands approved");
    }

    [TestMethod]
    public async Task AMemoLongerThanTheApiAllowsIsCutNotRejected()
    {
        var memo = (await Create(Planned(new string('x', 700)))).GetProperty("memo").GetString()!;

        Assert.AreEqual(500, memo.Length);
        StringAssert.EndsWith(memo, "…");
    }

    [TestMethod]
    public async Task MovingARowSendsTheNewAccountAndNothingElse()
    {
        var handler = new StubHandler();
        var api = new YnabApi(new HttpClient(handler), "token");

        await api.MoveAsync("plan-1", "t-1", "acc-b");

        Assert.AreEqual("PATCH", handler.Requests.Single().Method);

        var sent = TransactionOf(handler);
        Assert.AreEqual("acc-b", sent.GetProperty("account_id").GetString());
        Assert.AreEqual(1, sent.EnumerateObject().Count(), "a move rewrites nothing else on the row");
    }

    [TestMethod]
    public async Task ARefusalKeepsTheReasonTheApiGave()
    {
        var handler = new StubHandler(
            HttpStatusCode.BadRequest,
            """
            {"error":{"id":"400","name":"bad_request","detail":"cleared must be an allowed value"}}
            """
        );
        var api = new YnabApi(new HttpClient(handler), "token");

        var failure = await Assert.ThrowsExactlyAsync<YnabApiException>(() => api.CreateAsync("plan-1", Planned("memo")));

        StringAssert.Contains(failure.Message, "cleared must be an allowed value", "the reason is the message, not the status");
    }

    private static async Task<JsonElement> Create(PlannedCreate create)
    {
        var handler = new StubHandler();
        var api = new YnabApi(new HttpClient(handler), "token");

        Assert.AreEqual(StubHandler.NewId, await api.CreateAsync("plan-1", create));

        return TransactionOf(handler);
    }

    private static JsonElement TransactionOf(StubHandler handler) =>
        JsonDocument.Parse(handler.Requests.Single().Body).RootElement.GetProperty("transaction").Clone();

    private static PlannedCreate Planned(string memo) =>
        new(
            AccountId: "acc-a",
            AccountName: "DKB Giro",
            Date: new DateOnly(2026, 10, 23),
            Amount: -45.81m,
            PayeeName: "Kaufland",
            Memo: memo,
            ImportId: "ayc-20261023-abc",
            CategoryId: null
        );
}

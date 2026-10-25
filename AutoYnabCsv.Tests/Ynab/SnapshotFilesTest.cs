using AutoYnabCsv.Ynab;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Snapshot;

namespace Tests.Ynab;

[TestClass]
public class SnapshotFilesTest
{
    [TestMethod]
    public void ASavedSnapshotComesBackAsItWentIn()
    {
        var folder = TempFolder();

        SnapshotStore.Save(Sample.Snapshot(Sample.T("t1", "2026-10-23", -19.71m, "PayPal")), folder);
        var back = SnapshotStore.Load(folder);

        Assert.AreEqual("Household", back.PlanName);
        Assert.AreEqual("t1", back.Transactions.Single().Id);
        Assert.AreEqual(-19.71m, back.Transactions.Single().Amount.Amount);
    }

    [TestMethod]
    public void AnEmptyFolderSaysToReadThePlanFirst()
    {
        var failure = Assert.ThrowsExactly<SnapshotUnreadableException>(() => SnapshotStore.Load(TempFolder()));

        StringAssert.Contains(failure.Message, "ynab snapshot");
    }

    [TestMethod]
    public async Task ThePlanIsReadAMonthAtATimeWithoutGaps()
    {
        var gateway = new MonthsGateway();
        var since = new DateOnly(2026, 1, 1);

        await YnabSnapshotService.ReadAsync(gateway, "plan-1", since, say: null);

        var windows = gateway.Windows;
        Assert.AreEqual(since, windows[0].From);
        Assert.AreEqual(DateOnly.FromDateTime(DateTime.UtcNow), windows[^1].Until, "the walk reaches today");
        Assert.IsTrue(
            windows.Zip(windows.Skip(1)).All(pair => pair.First.Until.AddDays(1) == pair.Second.From),
            "no day is left unread between two requests"
        );
    }

    [TestMethod]
    public async Task AnUnknownPlanSaysWhichOneWasAsked()
    {
        var gateway = new MonthsGateway { PlanId = "plan-2" };

        var failure = await Assert.ThrowsExactlyAsync<PlanNotFoundException>(
            () => YnabSnapshotService.ReadAsync(gateway, "plan-1", new DateOnly(2026, 1, 1), say: null)
        );

        StringAssert.Contains(failure.Message, "plan-1");
    }

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ayc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A plan that remembers which days it was asked about, and answers nothing.</summary>
    private sealed class MonthsGateway : IYnabGateway
    {
        public string PlanId { get; init; } = "plan-1";

        public List<(DateOnly From, DateOnly Until)> Windows { get; } = [];

        public Task<IReadOnlyList<YnabPlanRef>> PlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YnabPlanRef>>([new YnabPlanRef(PlanId, "Household")]);

        public Task<IReadOnlyList<YnabAccount>> AccountsAsync(string planId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YnabAccount>>([]);

        public Task<IReadOnlyList<YnabCategory>> CategoriesAsync(string planId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YnabCategory>>([]);

        public Task<IReadOnlyList<YnabPayee>> PayeesAsync(string planId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YnabPayee>>([]);

        public Task<IReadOnlyList<YnabTransaction>> TransactionsAsync(
            string planId,
            DateOnly since,
            DateOnly until,
            CancellationToken cancellationToken = default
        )
        {
            Windows.Add((since, until));
            return Task.FromResult<IReadOnlyList<YnabTransaction>>([]);
        }

        public Task<string> CreateAsync(string planId, AutoYnabCsv.Ynab.Apply.PlannedCreate create, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<YnabTransaction> DeleteAsync(string planId, string transactionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CategorizeAsync(
            string planId,
            string transactionId,
            string categoryId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task MoveAsync(
            string planId,
            string transactionId,
            string accountId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task AssignAsync(
            string planId,
            DateOnly month,
            string categoryId,
            long budgetedMilliunits,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}

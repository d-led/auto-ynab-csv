using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Report;

namespace Tests.Ynab;

[TestClass]
public class PlanSummaryTest
{
    [TestMethod]
    public void APlanIsReadAsTablesOfWhatWouldHappen()
    {
        var markdown = PlanSummary.Markdown(Plan(Deletes: [Delete(-45.81m), Delete(-1200m)]));

        StringAssert.Contains(markdown, "## Remove — in the plan, not in the bank (2)");
        Assert.IsTrue(
            markdown.IndexOf("-1200.00", StringComparison.Ordinal) < markdown.IndexOf("-45.81", StringComparison.Ordinal),
            "the rows that change the most money are read first"
        );
    }

    [TestMethod]
    public void ALongListSaysHowManyItDidNotShow()
    {
        var markdown = PlanSummary.Markdown(Plan(Deletes: [.. Enumerable.Range(0, 30).Select(index => Delete(-index - 1m))]), largest: 5);

        StringAssert.Contains(markdown, "largest 5 of 30");
    }

    [TestMethod]
    public void ThePlanSaysWhatItWillDoBeforeAnyTable()
    {
        var markdown = PlanSummary.Markdown(Plan(Deletes: [Delete(-10m)], Creates: [Create(-10m)], Moves: [Move(-10m)]));

        StringAssert.Contains(markdown, "1 to add, 1 to move, 1 to remove, 0 to categorize");
    }

    [TestMethod]
    public void ThePlansOwnNotesAreReadBeforeTheTables()
    {
        var markdown = PlanSummary.Markdown(
            Plan(Deletes: [Delete(-10m)]) with { Notes = ["nine rows were left alone because they are ours", "and one because the export is silent"] }
        );

        StringAssert.Contains(markdown, "- nine rows were left alone because they are ours");
        Assert.IsTrue(
            markdown.IndexOf("nine rows", StringComparison.Ordinal) < markdown.IndexOf("## Remove", StringComparison.Ordinal),
            "what the plan could not decide comes before what it did"
        );
    }

    [TestMethod]
    public void ACellCannotBreakTheTable()
    {
        var markdown = PlanSummary.Markdown(Plan(Deletes: [Delete(-10m, memo: "two | pipes\nand a newline")]));

        Assert.AreEqual(
            1,
            markdown.Split('\n').Count(line => line.StartsWith("| 2026-10-23 |", StringComparison.Ordinal)),
            "a memo with a pipe or a newline still occupies one row"
        );
        StringAssert.Contains(markdown, "two / pipes and a newline");
    }

    private static RepairPlan Plan(
        IReadOnlyList<PlannedDelete>? Deletes = null,
        IReadOnlyList<PlannedCreate>? Creates = null,
        IReadOnlyList<PlannedMove>? Moves = null
    ) =>
        new(
            PlanId: "plan-1",
            CreatedAt: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
            Creates: Creates ?? [],
            Deletes: Deletes ?? [],
            Categories: [],
            Notes: [],
            Moves: Moves ?? []
        );

    private static PlannedDelete Delete(decimal amount, string memo = "memo") =>
        new(
            "Girokonto · 0001",
            Sample.T("t1", "2026-10-23", amount, "Some Shop") with { Memo = memo },
            "the export does not have this row"
        );

    private static PlannedCreate Create(decimal amount) =>
        new("acc-giro", "Girokonto · 0001", new DateOnly(2026, 10, 23), amount, "Some Shop", "memo", "ayc-1", null);

    private static PlannedMove Move(decimal amount) =>
        new("t1", "acc-a", "acc-b", "Girokonto · 0001", "n26", new DateOnly(2026, 10, 23), amount, "memo");
}

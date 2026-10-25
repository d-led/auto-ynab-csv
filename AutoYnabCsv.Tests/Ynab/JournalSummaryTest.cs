using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Report;

namespace Tests.Ynab;

[TestClass]
public class JournalSummaryTest
{
    [TestMethod]
    public void AnEmptyJournalSaysNothingWasApplied()
    {
        StringAssert.Contains(JournalSummary.Markdown([]), "Nothing has been applied");
    }

    [TestMethod]
    public void TheJournalIsCountedByKindAndReadsNewestLast()
    {
        var markdown = JournalSummary.Markdown([
            Entry("2026-10-25T15:25:42Z", JournalEntry.CreatedKind, "first thing"),
            Entry("2026-10-25T15:26:42Z", JournalEntry.DeletedKind, "second thing"),
            Entry("2026-10-25T15:27:42Z", JournalEntry.DeletedKind, "third thing"),
        ]);

        StringAssert.Contains(markdown, "2 deleted, 1 created");
        StringAssert.Contains(markdown, "From 2026-10-25 15:25 to 2026-10-25 15:27.");
        Assert.IsTrue(
            markdown.IndexOf("third thing", StringComparison.Ordinal) > markdown.IndexOf("first thing", StringComparison.Ordinal),
            "the newest row is the last one read"
        );
    }

    [TestMethod]
    public void ALongJournalShowsOnlyTheNewestAndSaysSo()
    {
        var entries = Enumerable
            .Range(0, 40)
            .Select(index => Entry($"2026-10-25T15:{index:00}:00Z", JournalEntry.MovedKind, $"row {index}"))
            .ToList();

        var markdown = JournalSummary.Markdown(entries, newest: 5);

        StringAssert.Contains(markdown, "The newest 5 of 40");
        Assert.IsFalse(markdown.Contains("row 1 ", StringComparison.Ordinal), "the oldest rows are not shown");
    }

    private static JournalEntry Entry(string at, string kind, string detail) =>
        new(DateTimeOffset.Parse(at), kind, detail, TransactionId: null, Created: null, Deleted: null);
}

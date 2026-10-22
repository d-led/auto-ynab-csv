using AutoYnabCsv.MailIndex;
using AutoYnabCsv.MailIndex.Correlation;

namespace Tests;

/// <summary>
/// The exports decide how much mail is worth reading. Everything the run asks for is derived from
/// the first and the last day a row carries.
/// </summary>
[TestClass]
public class MailRangeTest
{
    [TestMethod]
    public void TheRangeIsTheFirstAndTheLastDayOfTheRows()
    {
        var range = MailRange.Of([Entry(new DateOnly(2026, 10, 9)), Entry(new DateOnly(2026, 10, 2))]);

        Assert.AreEqual(new DateOnly(2026, 10, 2), range!.From);
        Assert.AreEqual(new DateOnly(2026, 10, 9), range.To);
    }

    [TestMethod]
    public void AnExportWithoutRowsHasNoRange()
    {
        Assert.IsNull(MailRange.Of([]));
    }

    [TestMethod]
    public void TheMarginIsTakenOnBothSides()
    {
        var range = MailRange.Of([Entry(new DateOnly(2026, 10, 2))])!;

        Assert.AreEqual(new DateTime(2026, 9, 30), range.Since(2).DateTime);
        Assert.AreEqual(new DateTime(2026, 10, 5), range.Until(2).DateTime);
    }

    [TestMethod]
    public void ASpanOlderThanTheLastSyncIsTheOneThatGetsRead()
    {
        var range = MailRange.Of([Entry(new DateOnly(2026, 9, 20))])!;
        var lastSync = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

        var window = MailRange.Window(range, lastSync, marginDays: 7, overlapDays: 7);

        Assert.AreEqual(range.Since(7).LocalDateTime, window!.Value.LocalDateTime);
    }

    [TestMethod]
    public void WithoutExportsTheLastSyncDecides()
    {
        var lastSync = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

        Assert.AreEqual(
            lastSync.AddDays(-7).LocalDateTime,
            MailRange.Window(null, lastSync, marginDays: 7, overlapDays: 7)!.Value.LocalDateTime
        );
    }

    [TestMethod]
    public void WithoutExportsAndWithoutASyncTheWholeStoreIsRead()
    {
        Assert.IsNull(MailRange.Window(null, lastSyncedAt: null, marginDays: 7, overlapDays: 7));
    }

    private static CorrelationEntry Entry(DateOnly day) =>
        new(0, day, "Markt", 12.34m);
}

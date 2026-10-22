using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Index;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class MailIndexStoreTest
{
    private string _path = string.Empty;
    private MailIndexStore _store = null!;

    [TestInitialize]
    public void OpenIndex()
    {
        _path = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"{Guid.NewGuid():N}.sqlite"
        );
        _store = MailIndexStore.Open(_path);
    }

    [TestCleanup]
    public void DeleteIndex()
    {
        _store.Dispose();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }

    [TestMethod]
    public void ARebuildStartsInAFileOfItsOwnInsteadOfKeepingWhatItForgot()
    {
        foreach (var number in Enumerable.Range(0, 50))
        {
            var file = MessageFile($"{number}.emlx", 200_000);
            _store.Upsert(
                file,
                Facts(
                    file.Path,
                    new DateTimeOffset(2024, 12, 25, 10, 0, 0, TimeSpan.Zero),
                    string.Concat(
                        Enumerable.Range(0, 4_000).Select(n => $"Artikel {number}/{n} Kabelbinder\n")
                    )
                )
            );
        }

        var built = DiskSize();

        _store.StartRebuild("rules-2");

        Assert.AreEqual(0, _store.Count);
        Assert.IsTrue(_store.RebuildUnfinished);
        Assert.AreEqual("rules-2", _store.Fingerprint);
        Assert.IsTrue(
            DiskSize() < built / 4,
            $"the rebuilt index is {DiskSize()} bytes, down from {built}"
        );
    }

    [TestMethod]
    public void ABoundedSyncDoesNotPruneWhatItDidNotSee()
    {
        var first = MessageFile("a.emlx", 10);
        var second = MessageFile("b.emlx", 10);

        MailIndexSync.Run(_store, [first], _ => Facts(first.Path), "rules-1");
        MailIndexSync.Run(
            _store,
            [second],
            _ => Facts(second.Path),
            "rules-1",
            pruneMissing: false
        );

        Assert.AreEqual(2, _store.Count);
    }

    [TestMethod]
    public void TheIndexRemembersHowFarItReaches()
    {
        var file = MessageFile("a.emlx", 10);

        MailIndexSync.Run(_store, [file], _ => Facts(file.Path), "rules-1");

        Assert.AreEqual(
            new DateTimeOffset(2024, 12, 25, 10, 0, 0, TimeSpan.Zero),
            _store.IndexedThrough
        );
        Assert.AreEqual(
            new DateTimeOffset(2024, 12, 25, 10, 0, 0, TimeSpan.Zero),
            _store.IndexedFrom
        );
        Assert.IsNotNull(_store.LastSyncedAt);
    }

    [TestMethod]
    public void AnEmptyIndexHasNoWatermark()
    {
        Assert.IsNull(_store.IndexedFrom);
        Assert.IsNull(_store.IndexedThrough);
        Assert.IsNull(_store.LastSyncedAt);
    }

    [TestMethod]
    public void ASecondSyncReadsNothingAgain()
    {
        var reads = 0;
        var file = MessageFile("a.emlx", 10);
        var facts = Facts(file.Path);

        MailIndexSync.Run(
            _store,
            [file],
            _ =>
            {
                reads++;
                return facts;
            },
            "rules-1"
        );
        var second = MailIndexSync.Run(
            _store,
            [file],
            _ =>
            {
                reads++;
                return facts;
            },
            "rules-1"
        );

        Assert.AreEqual(1, reads);
        Assert.AreEqual(1, second.Skipped);
        Assert.AreEqual(0, second.Indexed);
        Assert.AreEqual(1, _store.Count);
    }

    [TestMethod]
    public void ChangedRulesRebuildTheIndex()
    {
        var file = MessageFile("a.emlx", 10);
        var facts = Facts(file.Path);

        MailIndexSync.Run(_store, [file], _ => facts, "rules-1");
        var second = MailIndexSync.Run(_store, [file], _ => facts, "rules-2");

        Assert.IsTrue(second.Rebuilt);
        Assert.AreEqual(1, second.Indexed);
        Assert.AreEqual(1, _store.Count);
    }

    [TestMethod]
    public void FactsAndFieldsSurviveTheRoundTrip()
    {
        var file = MessageFile("a.emlx", 10);

        MailIndexSync.Run(_store, [file], _ => Facts(file.Path), "rules-1");

        var loaded = _store.LoadFacts().Single();

        Assert.AreEqual("Amazon", loaded.Vendor);
        Assert.AreEqual("302-1234567-8901234", loaded.OrderNumbers.Single());
        Assert.AreEqual(42.99m, loaded.Amounts.Single());
        Assert.AreEqual("302-1234567-8901234", loaded.Fields["orderNumber"]);
        Assert.AreEqual("302.1234567@amazon.de", loaded.Mail.MessageId);
    }

    [TestMethod]
    public void SearchFindsAWordInTheBody()
    {
        var file = MessageFile("a.emlx", 10);

        MailIndexSync.Run(_store, [file], _ => Facts(file.Path), "rules-1");

        var hits = _store.Search("Kabel");

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(file.Path, hits[0].Path);
    }

    [TestMethod]
    public void OnlyTheFactsInsideTheAskedWindowAreLoaded()
    {
        var inWindow = Facts("/mail/inside.emlx", new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
        var tooNew = Facts("/mail/newer.emlx", new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));
        var tooOld = Facts("/mail/older.emlx", new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));

        foreach (var facts in new[] { inWindow, tooNew, tooOld })
        {
            _store.Upsert(
                new EmlxFile(facts.Mail.Path, facts.Mail.ReceivedAt, 1),
                facts
            );
        }

        var loaded = _store.LoadFacts(
            since: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            until: new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)
        );

        CollectionAssert.AreEqual(new[] { inWindow.Mail.Path }, loaded.Select(f => f.Mail.Path).ToArray());
    }

    [TestMethod]
    public void TheIndexSizeGrowsWithWhatItHolds()
    {
        var before = _store.DiskBytes;
        var batch = Enumerable
            .Range(0, 50)
            .Select(index =>
            {
                var path = MessageFile($"size-{index}.emlx", 10).Path;
                return (MessageFile($"size-{index}.emlx", 10), Facts(path, new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero)));
            })
            .ToList();

        _store.UpsertBatch(batch);

        Assert.IsTrue(_store.DiskBytes > before, "the batch put bytes on disk");
    }

    [TestMethod]
    public void AnOrderNumberIsFoundAsItIsWritten()
    {
        var file = MessageFile("order.emlx", 10);
        _store.Upsert(file, Facts(file.Path, new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero), "Order # 028-1234567-8901234"));

        var hits = _store.Search("028-1234567-8901234");

        Assert.AreEqual(1, hits.Count);
    }

    [TestMethod]
    public void AQueryWithSyntaxInItIsNotAnError()
    {
        var file = MessageFile("order.emlx", 10);
        _store.Upsert(file, Facts(file.Path, new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero), "Ihre Bestellung"));

        Assert.AreEqual(0, _store.Search("\"unbalanced").Count);
        Assert.AreEqual(0, _store.Search("AND OR NOT").Count);
        Assert.AreEqual(0, _store.Search("   ").Count);
    }

    /// <summary>What the index takes on disk, asked of the disk rather than the store's cache.</summary>
    private long DiskSize() =>
        new[] { _path, _path + "-wal" }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);

    private static EmlxFile MessageFile(string name, long length) =>
        new(Path.Combine("/tmp", name), new DateTimeOffset(2024, 12, 25, 10, 0, 0, TimeSpan.Zero), length);

    private static MailFacts Facts(string path) =>
        Facts(path, new DateTimeOffset(2024, 12, 25, 10, 0, 0, TimeSpan.Zero));

    private static MailFacts Facts(string path, DateTimeOffset received) =>
        Facts(path, received, "USB-C Kabel");

    private static MailFacts Facts(string path, DateTimeOffset received, string body) =>
        new(
            new MailRecord(
                path,
                received,
                "Ihre Bestellung",
                "order@amazon.de",
                string.Empty,
                body,
                "302.1234567@amazon.de"
            ),
            "Amazon",
            ["302-1234567-8901234"],
            [42.99m],
            new Dictionary<string, string> { ["orderNumber"] = "302-1234567-8901234" }
        );
}

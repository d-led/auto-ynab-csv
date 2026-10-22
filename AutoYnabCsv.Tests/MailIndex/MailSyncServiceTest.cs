using AutoYnabCsv.MailIndex;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Index;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class MailSyncServiceTest
{
    private static readonly DateTimeOffset Received = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ASyncWalksTheStoreAndReportsWhatItCovers()
    {
        WithStore(
            root =>
            {
                Write(root, "1.emlx");
                Write(root, "2.partial.emlx");
            },
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);

                var report = MailSyncService.Sync(
                    index,
                    root,
                    RuleSetLoader.Shipped()
                );

                Assert.AreEqual(1, report.Total);
                Assert.AreEqual(1, report.Partial);
                Assert.AreEqual(Received, report.IndexedFrom);
                Assert.AreEqual(Received, report.IndexedThrough);
                StringAssert.Contains(report.Coverage, "1 messages covering 2026-10-02");
            }
        );
    }

    [TestMethod]
    public void ASyncSaysHowFarItGotAndWhatItCouldNotRead()
    {
        WithStore(
            root =>
            {
                Write(root, "1.emlx");
                Unreadable(root, "2.emlx");
            },
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var seen = new List<SyncProgress>();

                var report = MailSyncService.Sync(
                    index,
                    root,
                    RuleSetLoader.Shipped(),
                    progress: seen.Add
                );

                Assert.AreEqual(1, report.Result.Indexed);
                Assert.AreEqual(1, report.Result.Unreadable);
                CollectionAssert.AreEqual(
                    new[] { $"NoLengthLine  {Path.Combine(MessageFolder(root), "2.emlx")}" },
                    report.UnreadableLines().ToArray()
                );
                Assert.AreEqual(2, seen[^1].Seen);
                Assert.AreEqual(2, seen[^1].Total);
                StringAssert.EndsWith(seen[^1].Reading, "2.emlx");

                // The first word about a message comes before it is read, so a slow one is named
                // while it is slow rather than once it is done.
                StringAssert.EndsWith(seen[0].Reading, "1.emlx");
                Assert.AreEqual(0, seen[0].Indexed);
                Assert.IsTrue(seen[^1].ReadMilliseconds >= 0);
            }
        );
    }

    [TestMethod]
    public void ALongListOfUnreadableMessagesStillReadsLikeAList()
    {
        // Naming every one of them would bury the first, which is the one a person acts on.
        WithStore(
            root =>
            {
                foreach (var name in Enumerable.Range(1, 7).Select(one => $"{one}.emlx"))
                {
                    Unreadable(root, name);
                }
            },
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);

                var report = MailSyncService.Sync(index, root, RuleSetLoader.Shipped());

                Assert.AreEqual(7, report.Result.Unreadable);
                Assert.AreEqual(MailSyncReport.Named + 1, report.UnreadableLines().Count);
                StringAssert.Contains(report.UnreadableLines()[^1], "and 2 more");
            }
        );
    }

    [TestMethod]
    public void WhatWasIndexedCanBeReadAgain()
    {
        WithStore(
            root => Write(root, "1.emlx"),
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var rules = RuleSetLoader.Shipped();
                MailSyncService.Sync(index, root, rules);

                var facts = MailSyncService.Read(
                    Path.Combine(root, "V10", "INBOX.mbox", "Data", "Messages", "1.emlx"),
                    rules
                );

                Assert.IsNotNull(facts);
                Assert.AreEqual("Amazon", facts.Vendor);
            }
        );
    }

    [TestMethod]
    public void AMessageWithoutABodyIsCompletedFromTheMailApp()
    {
        WithStore(
            root => Write(root, "1.emlx", body: string.Empty),
            (root, _) =>
            {
                var rules = RuleSetLoader.Shipped();
                var path = Path.Combine(root, "V10", "INBOX.mbox", "Data", "Messages", "1.emlx");

                Assert.AreEqual(0, MailSyncService.Read(path, rules)!.Amounts.Count);

                var completed = MailSyncService.Lookup(rules, new FakeBody("Gesamtsumme: EUR 42,99")).WithBody(
                    MailSyncService.Read(path, rules)!
                );

                CollectionAssert.AreEqual(new[] { 42.99m }, completed!.Amounts.ToArray());
            }
        );
    }

    [TestMethod]
    public void ASecondRunOverTheSameSpanReadsNothingAgain()
    {
        WithStore(
            root =>
            {
                Write(root, "1.emlx", writtenAt: Received);
                Write(root, "2.emlx", writtenAt: Received);
            },
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var rules = RuleSetLoader.Shipped();
                var span = Received.AddDays(-1);

                MailSyncService.Sync(index, root, rules, span);
                var again = MailSyncService.Sync(index, root, rules, span);

                Assert.AreEqual(0, again.Result.Indexed);
                Assert.AreEqual(2, again.Result.Skipped);
                Assert.AreEqual(2, index.Count);
            }
        );
    }

    [TestMethod]
    public void ARunOverAnotherSpanKeepsWhatIsAlreadyIndexed()
    {
        WithStore(
            root =>
            {
                Write(root, "old.emlx", writtenAt: Received.AddDays(-60));
                Write(root, "new.emlx", writtenAt: Received);
            },
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var rules = RuleSetLoader.Shipped();

                var recent = MailSyncService.Sync(index, root, rules, Received.AddDays(-1));
                Assert.AreEqual(1, recent.Result.Indexed);

                var older = MailSyncService.Sync(index, root, rules, Received.AddDays(-90));

                Assert.IsFalse(older.Result.Rebuilt);
                Assert.AreEqual(1, older.Result.Indexed);
                Assert.AreEqual(2, index.Count);
            }
        );
    }

    [TestMethod]
    public void AnInterruptedRebuildIsResumedInsteadOfClearedAgain()
    {
        WithStore(
            root =>
            {
                Write(root, "1.emlx", writtenAt: Received);
                Write(root, "2.emlx", writtenAt: Received);
            },
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var rules = RuleSetLoader.Shipped();
                MailSyncService.Sync(index, root, rules);

                // A rebuild that was killed half way: the fingerprint is on record, one message is
                // in, the other never got there — and the files are older than the week a routine
                // run looks back.
                index.StartRebuild(rules.Fingerprint);
                var first = Path.Combine(
                    root, "V10", "INBOX.mbox", "Data", "Messages", "1.emlx"
                );
                index.Upsert(
                    new EmlxFile(first, Received, new FileInfo(first).Length),
                    MailSyncService.Read(first, rules)!
                );

                var resumed = MailSyncService.Sync(index, root, rules);

                Assert.IsFalse(resumed.Result.Rebuilt);
                Assert.AreEqual(1, resumed.Result.Skipped);
                Assert.AreEqual(1, resumed.Result.Indexed);
                Assert.AreEqual(2, index.Count);
                Assert.IsFalse(index.RebuildUnfinished);
            }
        );
    }

    [TestMethod]
    public void ASyncSaysWhetherItRebuildsOrReadsAWindow()
    {
        WithStore(
            root => Write(root, "1.emlx", writtenAt: Received),
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var starts = new List<SyncStart>();

                MailSyncService.Sync(
                    index,
                    root,
                    RuleSetLoader.Shipped(),
                    Received.AddDays(-1),
                    starting: starts.Add
                );
                MailSyncService.Sync(
                    index,
                    root,
                    RuleSetLoader.Shipped(),
                    Received.AddDays(-1),
                    starting: starts.Add
                );

                Assert.IsTrue(starts[0].Rebuilding, "a store without a fingerprint is rebuilt");
                Assert.IsFalse(starts[1].Rebuilding);
                Assert.AreEqual(Received.AddDays(-1).LocalDateTime, starts[1].Since!.Value.LocalDateTime);
                Assert.AreEqual(1, starts[1].Known);
            }
        );
    }

    private static string MessageFolder(string root) =>
        Path.Combine(root, "V10", "INBOX.mbox", "Data", "Messages");

    /// <summary>A file that sits where a message sits but is not one.</summary>
    private static void Unreadable(string root, string name)
    {
        Directory.CreateDirectory(MessageFolder(root));
        File.WriteAllText(Path.Combine(MessageFolder(root), name), "not a message");
    }

    private static void Write(
        string root,
        string name,
        string body = "Gesamtsumme: EUR 42,99",
        DateTimeOffset? writtenAt = null
    )
    {
        var directory = MessageFolder(root);
        Directory.CreateDirectory(directory);

        var payload = TestEmlx.Message(
            "Ihre Bestellung 302-1234567-8901234",
            "order@amazon.de",
            body,
            Received
        );
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, TestEmlx.Build(payload));

        if (writtenAt is { } when)
        {
            File.SetLastWriteTimeUtc(path, when.UtcDateTime);
        }
    }

    [TestMethod]
    public void AFetchedBodyIsKeptAsItsFactsAndFetchedOnlyOnce()
    {
        WithStore(
            root => Write(root, "1.emlx", body: string.Empty),
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var rules = RuleSetLoader.Shipped();
                var bodies = new CountingBody("Gesamtsumme: EUR 42,99");
                var lookup = MailSyncService.Lookup(rules, bodies, index);
                var facts = MailSyncService.Read(
                    Path.Combine(root, "V10", "INBOX.mbox", "Data", "Messages", "1.emlx"),
                    rules
                )!;

                Assert.AreEqual(0, facts.Amounts.Count);
                Assert.AreEqual(42.99m, lookup.WithBody(facts)!.Amounts.Single());
                Assert.AreEqual(42.99m, lookup.WithBody(facts)!.Amounts.Single());
                Assert.AreEqual(1, bodies.Calls);

                var stored = index.LoadFacts().Single();
                Assert.AreEqual(42.99m, stored.Amounts.Single(), "the extraction is kept");
                Assert.AreEqual(0, index.Search("Gesamtsumme").Count, "the body is not");
            }
        );
    }

    private sealed class FakeBody(string body) : IMailBodySource
    {
        public string? BodyOf(MailRecord mail) => body;
    }

    private sealed class CountingBody(string body) : IMailBodySource
    {
        public int Calls { get; private set; }

        public string? BodyOf(MailRecord mail)
        {
            Calls++;
            return body;
        }
    }

    [TestMethod]
    public void AnInterruptedRebuildIsAnnouncedAndWalksTheStoreAgain()
    {
        // A rebuild that was killed left its marker behind: the days since the last sync say
        // nothing about what is missing, so everything is read again — and the run says so,
        // instead of looking like a sync that decided to redo the mailbox.
        WithStore(
            root =>
            {
                Write(root, "1.emlx");
                Write(root, "2.emlx");
            },
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var rules = RuleSetLoader.Shipped();
                MailSyncService.Sync(index, root, rules);
                index.RebuildUnfinished = true;

                var starts = new List<SyncStart>();
                MailSyncService.Sync(index, root, rules, starting: starts.Add);

                Assert.IsTrue(starts.Single().Resuming);
                Assert.IsNull(starts.Single().Since);
            }
        );
    }

    [TestMethod]
    public void AMessageThatWillNotReadIsGivenUpOnInsteadOfStoppingTheSync()
    {
        // A 4 KB file the system would not hand over held a whole run for six minutes. One such
        // message may not stop the other eighty thousand.
        WithStore(
            root => Write(root, "1.emlx"),
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                var file = new EmlxFile(
                    Path.Combine(MessageFolder(root), "1.emlx"),
                    DateTimeOffset.UtcNow,
                    0
                );
                var blamed = new List<UnreadableMessage>();
                var started = Environment.TickCount64;

                var result = MailIndexSync.Run(
                    index,
                    [file],
                    _ =>
                    {
                        Thread.Sleep(5_000);
                        return null;
                    },
                    RuleSetLoader.Shipped().Fingerprint,
                    unreadableMessage: blamed.Add,
                    readTimeoutMilliseconds: 100
                );

                Assert.IsTrue(Environment.TickCount64 - started < 4_000);
                Assert.AreEqual(1, result.Unreadable);
                Assert.AreEqual("TimedOut", blamed.Single().Reason);
            }
        );
    }

    [TestMethod]
    public void TheTimeAMessageGetsGrowsWithItsSizeWithinBounds()
    {
        // A broken file never gets faster, so a small one gets a short leash; the largest message
        // in this mailbox is 62 MB and still reads inside the ceiling.
        Assert.AreEqual(2_005, MailIndexSync.TimeoutFor(5_000));
        Assert.AreEqual(20_000, MailIndexSync.TimeoutFor(62_000_000));
        Assert.AreEqual(2_000, MailIndexSync.TimeoutFor(0));
    }

    [TestMethod]
    public void ReadingAFileThatIsNoLongerThereIsNotAFailure()
    {
        // Mail moves, deletes and re-downloads messages while a long walk runs, so the file named
        // by the walk can be gone by the time it is read. That is nothing to index, not a crash.
        var gone = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            Guid.NewGuid().ToString("N"),
            "gone.emlx"
        );

        Assert.IsNull(MailSyncService.Read(gone, RuleSetLoader.Shipped()));
    }

    [TestMethod]
    public void AMessageDatedInTheFutureIsDatedByItsFileInstead()
    {
        // A broken import once wrote 2242-03-19 for a whole batch of messages. Such a message
        // would sit outside every search window and stretch the index to the year 2243.
        WithStore(
            root =>
            {
                var directory = MessageFolder(root);
                Directory.CreateDirectory(directory);
                var payload = TestEmlx.Message(
                    "Re: Tresor",
                    "archive@example.com",
                    "nur ein Archiv",
                    new DateTimeOffset(2242, 3, 19, 13, 28, 33, TimeSpan.Zero)
                );
                File.WriteAllBytes(
                    Path.Combine(directory, "1.emlx"),
                    TestEmlx.Build(payload, receivedAt: 8_592_000_000)
                );
            },
            (root, indexPath) =>
            {
                using var index = MailIndexStore.Open(indexPath);
                MailSyncService.Sync(index, root, RuleSetLoader.Shipped());

                var stored = index.LoadFacts().Single().Mail.ReceivedAt;

                Assert.IsTrue(stored.Year >= 1990 && stored <= DateTimeOffset.UtcNow.AddDays(1));
            }
        );
    }

    private static void WithStore(Action<string> prepare, Action<string, string> use)
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            Guid.NewGuid().ToString("N")
        );
        var root = Path.Combine(folder, "Mail");
        Directory.CreateDirectory(root);

        try
        {
            prepare(root);
            use(root, Path.Combine(folder, "mail-index.sqlite"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

using System.Diagnostics;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Index;
using AutoYnabCsv.MailIndex.Mail;

namespace AutoYnabCsv.MailIndex.Index;

public sealed record SyncResult(
    int Indexed,
    int Skipped,
    int Removed,
    int Unreadable,
    bool Rebuilt
);

/// <summary>How far a sync has come, so a long first run is not a silent one.</summary>
public sealed record SyncProgress(
    int Seen,
    int Total,
    int Indexed,
    int Skipped,
    int Unreadable,
    long Bytes,
    string Reading,
    long ReadMilliseconds,
    long ReadingBytes,
    bool TimedOut
);

/// <summary>
/// Brings the index up to date. A message is read only when it is new, when its file changed,
/// or when the extraction rules changed — never on a plain second run.
/// </summary>
public static class MailIndexSync
{
    /// <summary>
    /// How many messages are written before the transaction is committed. A batch bounds both the
    /// time a commit costs and the work an interruption can throw away.
    /// </summary>
    private const int BatchSize = 250;

    /// <summary>
    /// What one message may take. A file the system will not hand over never gets faster, so the
    /// floor is what a small message needs plus room to spare; a large message buys more, which is
    /// why the budget grows with the file instead of being one number for all of them.
    /// </summary>
    public static int TimeoutFor(long bytes) =>
        (int)Math.Clamp(FloorMilliseconds + (bytes / 1_000), FloorMilliseconds, CeilingMilliseconds);

    /// <summary>Enough for any ordinary message, far below a broken one's wait.</summary>
    private const int FloorMilliseconds = 2_000;

    /// <summary>The largest message in this mailbox is 62 MB; even that is read well within this.</summary>
    private const int CeilingMilliseconds = 20_000;

    public static SyncResult Run(
        MailIndexStore store,
        IEnumerable<EmlxFile> files,
        Func<EmlxFile, MailFacts?> read,
        string rulesFingerprint,
        bool forceFull = false,
        bool pruneMissing = true,
        Action<SyncProgress>? progress = null,
        Action<UnreadableMessage>? unreadableMessage = null,
        int? readTimeoutMilliseconds = null
    )
    {
        var batch = files as IReadOnlyList<EmlxFile> ?? files.ToArray();
        var known = store.KnownFiles();
        var rebuilt = forceFull || store.Fingerprint != rulesFingerprint;

        if (rebuilt)
        {
            store.StartRebuild(rulesFingerprint);
        }
        else
        {
            store.Fingerprint = rulesFingerprint;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new List<(EmlxFile File, MailFacts Facts)>(BatchSize);
        var indexed = 0;
        var skipped = 0;
        var unreadable = 0;

        SyncProgress Report(EmlxFile file, long readMilliseconds, bool timedOut) =>
            new(
                seen.Count,
                batch.Count,
                indexed,
                skipped,
                unreadable,
                store.DiskBytes,
                file.Path,
                readMilliseconds,
                file.Length,
                timedOut
            );

        foreach (var file in batch)
        {
            seen.Add(file.Path);

            if (Unchanged(file, known, rebuilt))
            {
                skipped++;
                progress?.Invoke(Report(file, 0, false));
                continue;
            }

            // Named before it is read: a message that takes minutes has to be on screen while it
            // is being read, not only once it is done.
            progress?.Invoke(Report(file, 0, false));

            var (facts, took, timedOut) = ReadTimed(
                file,
                read,
                readTimeoutMilliseconds ?? TimeoutFor(file.Length)
            );

            if (timedOut)
            {
                unreadableMessage?.Invoke(new UnreadableMessage(file.Path, "TimedOut"));
            }

            if (facts is null)
            {
                unreadable++;
            }
            else
            {
                indexed++;
                Collect(store, pending, file, facts);
            }

            progress?.Invoke(Report(file, took, timedOut));
        }

        store.UpsertBatch(pending);

        var removed = rebuilt || !pruneMissing ? 0 : store.RemoveMissing(seen);
        store.MarkSynced(DateTimeOffset.UtcNow);
        store.RebuildUnfinished = false;

        return new SyncResult(indexed, skipped, removed, unreadable, rebuilt);
    }

    /// <summary>A message already read, unchanged in content and time, needs reading no more.</summary>
    private static bool Unchanged(
        EmlxFile file,
        IReadOnlyDictionary<string, FileState> known,
        bool rebuilt
    ) =>
        !rebuilt
        && known.TryGetValue(file.Path, out var state)
        && state.ModifiedTicks == file.LastWriteUtc.UtcTicks
        && state.Length == file.Length;

    private static (MailFacts? Facts, long Milliseconds, bool TimedOut) ReadTimed(
        EmlxFile file,
        Func<EmlxFile, MailFacts?> read,
        int timeoutMilliseconds
    )
    {
        var started = Stopwatch.GetTimestamp();
        var reading = Task.Run(() => read(file));

        if (reading.Wait(timeoutMilliseconds))
        {
            // GetResult, not Result: an unreadable file must arrive as itself, not wrapped in an
            // AggregateException the caller cannot recognize.
            return (reading.GetAwaiter().GetResult(), Elapsed(started), false);
        }

        // The read is left behind on its own thread: a file that never arrives cannot be
        // interrupted, and waiting for it is what used to make the whole run look dead.
        return (null, Elapsed(started), true);
    }

    private static long Elapsed(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>Writes a full batch when the batch is full, so a commit stays short.</summary>
    private static void Collect(
        MailIndexStore store,
        List<(EmlxFile File, MailFacts Facts)> pending,
        EmlxFile file,
        MailFacts facts
    )
    {
        pending.Add((file, facts));

        if (pending.Count < BatchSize)
        {
            return;
        }

        store.UpsertBatch(pending);
        pending.Clear();
    }
}

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
    bool Rebuilt,
    bool Cancelled = false
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
        int? readTimeoutMilliseconds = null,
        CancellationToken cancellation = default
    ) =>
        new Sweep(
            store,
            files as IReadOnlyList<EmlxFile> ?? files.ToArray(),
            read,
            rulesFingerprint,
            forceFull,
            pruneMissing,
            progress,
            unreadableMessage,
            readTimeoutMilliseconds
        ).Execute(cancellation);

    /// <summary>
    /// One pass over the listed messages. The counters, the batch and the store calls live here, so
    /// the pass reads as what it does: read a message, keep what it says, or leave it for the
    /// report.
    /// </summary>
    private sealed class Sweep(
        MailIndexStore store,
        IReadOnlyList<EmlxFile> batch,
        Func<EmlxFile, MailFacts?> read,
        string rulesFingerprint,
        bool forceFull,
        bool pruneMissing,
        Action<SyncProgress>? progress,
        Action<UnreadableMessage>? unreadableMessage,
        int? readTimeoutMilliseconds
    )
    {
        private readonly IReadOnlyDictionary<string, FileState> _known = store.KnownFiles();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<(EmlxFile File, MailFacts Facts)> _pending = new(BatchSize);
        private bool _rebuilt = forceFull;

        /// <summary>
        /// Facts written by other rules are stale, so the messages this pass covers are read again
        /// even though their files did not change. Those outside the pass keep what they have and are
        /// refreshed when a run reaches their dates — which is why a rule change no longer costs a
        /// whole mailbox.
        /// </summary>
        private readonly bool _rulesChanged = store.Fingerprint != rulesFingerprint;

        private int _indexed;
        private int _skipped;
        private int _unreadable;

        public SyncResult Execute(CancellationToken cancellation)
        {
            if (_rebuilt)
            {
                store.StartRebuild(rulesFingerprint);
            }
            else
            {
                store.Fingerprint = rulesFingerprint;
            }

            foreach (var file in batch)
            {
                if (cancellation.IsCancellationRequested)
                {
                    store.UpsertBatch(_pending);
                    return new SyncResult(
                        _indexed,
                        _skipped,
                        0,
                        _unreadable,
                        _rebuilt,
                        Cancelled: true
                    );
                }

                Read(file);
            }

            store.UpsertBatch(_pending);

            var removed = _rebuilt || !pruneMissing ? 0 : store.RemoveMissing(_seen);
            store.MarkSynced(DateTimeOffset.UtcNow);
            store.RebuildUnfinished = false;

            return new SyncResult(_indexed, _skipped, removed, _unreadable, _rebuilt);
        }

        private void Read(EmlxFile file)
        {
            _seen.Add(file.Path);

            if (Unchanged(file))
            {
                _skipped++;
                Report(file, 0, false);
                return;
            }

            // Named before it is read: a message that takes minutes has to be on screen while it
            // is being read, not only once it is done.
            Report(file, 0, false);

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
                _unreadable++;
            }
            else
            {
                _indexed++;
                Collect(file, facts);
            }

            Report(file, took, timedOut);
        }

        /// <summary>A message already read, unchanged in content and time, needs reading no more.</summary>
        private bool Unchanged(EmlxFile file) =>
            !_rebuilt
            && !_rulesChanged
            && _known.TryGetValue(file.Path, out var state)
            && state.ModifiedTicks == file.LastWriteUtc.UtcTicks
            && state.Length == file.Length;

        private void Report(EmlxFile file, long readMilliseconds, bool timedOut)
        {
            if (progress is null)
            {
                return;
            }

            progress(
                new SyncProgress(
                    _seen.Count,
                    batch.Count,
                    _indexed,
                    _skipped,
                    _unreadable,
                    store.DiskBytes,
                    file.Path,
                    readMilliseconds,
                    file.Length,
                    timedOut
                )
            );
        }

        /// <summary>Writes a full batch when the batch is full, so a commit stays short.</summary>
        private void Collect(EmlxFile file, MailFacts facts)
        {
            _pending.Add((file, facts));

            if (_pending.Count < BatchSize)
            {
                return;
            }

            store.UpsertBatch(_pending);
            _pending.Clear();
        }
    }

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
}

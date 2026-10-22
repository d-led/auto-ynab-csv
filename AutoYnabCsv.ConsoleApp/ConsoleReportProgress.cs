using System.Diagnostics;
using System.Globalization;
using AutoYnabCsv.MailIndex;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Index;

namespace AutoYnabCsv.App;

/// <summary>
/// A report run as the console shows it: why the sync reads what it reads, the sync line that
/// rewrites itself, how many stubs it has to read, and each entry it goes looking for. A run that
/// takes minutes should never look stuck, and two runs that read different amounts should say why.
/// </summary>
internal static class ConsoleReportProgress
{
    /// <summary>
    /// How often the line may be redrawn. Time, not a file count: reading a message can cost ten
    /// times what skipping one costs, so a count-based cadence makes the line crawl exactly when
    /// there is most to watch.
    /// </summary>
    private const int RefreshMilliseconds = 250;

    /// <summary>
    /// How often the run says it is still alive. A message body comes back from Mail when it comes
    /// back; in between, silence reads as a hang, so the clock speaks for it.
    /// </summary>
    private const int HeartbeatMilliseconds = 5_000;

    /// <summary>
    /// A message that took real time is named on a line of its own. It is the one fact a later
    /// run cannot recover: the counter forgets it, the log should not.
    /// </summary>
    private const int SlowMessageMilliseconds = 2_000;

    private static readonly object Output = new();

    private static long _lastPrinted;
    private static long _startedAt;
    private static bool _open;

    private static long _phaseStartedAt;
    private static int _rows;
    private static int _asked;
    private static int _bodies;
    private static Timer? _heartbeat;
    private static Timer? _alive;

    /// <summary>What is on screen now, so the watchdog can repeat it without guessing.</summary>
    private static string _status = string.Empty;

    /// <summary>The message being read right now; empty while the store is still being listed.</summary>
    private static string _reading = string.Empty;

    public static ReportProgress Hooks { get; } =
        new(Starting, Sync, Stubs, Entry) { BodyRead = BodyRead, Rows = Rows };

    /// <summary>
    /// Ends a line that was being rewritten, so whatever is printed next — a summary, an error —
    /// does not land on top of it. A run that ends after one of those lines would otherwise look
    /// as if it had swallowed its own result.
    /// </summary>
    public static void Flush()
    {
        Alive(false);
        Heartbeat(beating: false);
        Say(string.Empty, ownLine: false);
    }

    /// <summary>What the walk covers, said before it starts rather than guessed from the counter.</summary>
    private static void Starting(SyncStart start)
    {
        _startedAt = Environment.TickCount64;
        _lastPrinted = _startedAt;
        _status = Announcement(start);
        _reading = string.Empty;

        Say(_status);
        Alive(true);
    }

    /// <summary>
    /// Why so many messages are read: the rules changed, an earlier rebuild was interrupted, or
    /// the run really did ask for everything. A whole-store walk with no reason reads as a bug.
    /// </summary>
    private static string Announcement(SyncStart start) =>
        start.Rebuilding
            ? "Mail:    rebuilding the index — the extraction rules changed since it was built."
            : start.Resuming
                ? "Mail:    resuming the rebuild that was interrupted — the whole store is read again."
                : start.Since is { } since
                    ? $"Mail:    reading messages written since {Day(since)} "
                        + $"({Number(start.Known)} in the index already)."
                    : $"Mail:    reading the whole store ({Number(start.Known)} in the index already).";

    private static void Sync(SyncProgress progress)
    {
        if (progress.Total == 0)
        {
            return;
        }

        var finished = progress.Seen == progress.Total;
        var now = Environment.TickCount64;

        // Named before the line is throttled: the watchdog has to name the message being read
        // now, not the last one that happened to be drawn half a second ago.
        _reading = finished ? string.Empty : Path.GetFileName(progress.Reading);

        if (!finished && now - _lastPrinted < RefreshMilliseconds)
        {
            return;
        }

        _lastPrinted = now;

        _status =
            $"  {Number(progress.Seen)}/{Number(progress.Total)} messages, "
            + $"{Number(progress.Indexed)} indexed, "
            + $"{Number(progress.Skipped)} already known, "
            + $"{Number(progress.Unreadable)} unreadable, "
            + $"{ConsoleFormat.Size(progress.Bytes)} on disk, "
            + $"{(finished ? "took" : "at")} {ConsoleFormat.Duration(now - _startedAt)}";

        Rewrite($"{_status}   ");

        if (progress.ReadMilliseconds >= SlowMessageMilliseconds)
        {
            var verdict = progress.TimedOut ? " — given up on, it would not read" : string.Empty;
            Say(
                $"         slow     {progress.Reading} "
                    + $"({ConsoleFormat.Size(progress.ReadingBytes)}) "
                    + $"took {ConsoleFormat.Duration(progress.ReadMilliseconds)}{verdict}"
            );
        }

        if (finished)
        {
            Flush();
        }
    }

    /// <summary>
    /// How much of the export needs the mailbox at all. Said before the searching starts, because
    /// that is when a run looks stuck: a minute of silence is a minute of work with no witness.
    /// </summary>
    private static void Rows(int count)
    {
        _rows = count;
        _asked = 0;
        _bodies = 0;
        _phaseStartedAt = Environment.TickCount64;

        Alive(false);

        Say(
            count switch
            {
                0 => "Search:  every row says what it is; nothing to look up.",
                1 => "Search:  1 row says too little — looking it up. Every other row stays as the bank wrote it.",
                _ => $"Search:  {Number(count)} rows say too little — looking them up. Every other row stays as the bank wrote it.",
            }
        );

        if (count > 0 && !MailIsRunning())
        {
            Say(
                "         Mail is not running — a body cannot be read until it is, "
                    + "and every message that needs one waits for the timeout."
            );
        }

        Heartbeat(count > 0);
    }

    private static bool MailIsRunning() => Process.GetProcessesByName("Mail").Length > 0;

    /// <summary>What the search is doing right now, whether or not the last step has finished.</summary>
    private static string Phase() =>
        $"{Number(Volatile.Read(ref _asked))}/{Number(_rows)} rows asked, "
        + $"{Number(Volatile.Read(ref _bodies))} bodies read, "
        + $"{ConsoleFormat.Duration(Environment.TickCount64 - _phaseStartedAt)} so far";

    private static void Beat() => Rewrite($"Search:  {Phase()}…   ");

    private static void Stubs(int count) =>
        Say($"         checking {Number(count)} headers-only messages for a match…");

    private static void Entry(EntrySearch entry)
    {
        Interlocked.Increment(ref _asked);
        Say($"  {entry.Describe()}");
    }

    /// <summary>
    /// Reading a body from Mail is the slowest thing a run does and it happens one message at a
    /// time, so the count is redrawn as it climbs instead of appearing once the waiting is over.
    /// </summary>
    private static void BodyRead(int count)
    {
        Volatile.Write(ref _bodies, count);
        Rewrite($"Search:  {Phase()}…   ");
    }

    private static void Heartbeat(bool beating)
    {
        _heartbeat?.Dispose();
        _heartbeat = beating
            ? new Timer(
                _ => Beat(),
                null,
                HeartbeatMilliseconds,
                HeartbeatMilliseconds
            )
            : null;
    }

    /// <summary>
    /// Keeps the sync line honest. One message can take minutes — a body the disk has to find, a
    /// store the system is still listing — and a number that stops moving reads as a hang. The
    /// age and the message being read say it is still working.
    /// </summary>
    private static void Alive(bool running)
    {
        _alive?.Dispose();
        _alive = running
            ? new Timer(
                _ => Watch(),
                null,
                HeartbeatMilliseconds,
                HeartbeatMilliseconds
            )
            : null;
    }

    private static void Watch()
    {
        var idle = Environment.TickCount64 - _lastPrinted;
        if (idle < HeartbeatMilliseconds)
        {
            return;
        }

        var on = _reading.Length > 0 ? $" on {_reading}" : string.Empty;
        Rewrite($"{_status}   (waiting {ConsoleFormat.Duration(idle)}{on})   ");
    }

    /// <summary>
    /// Names the messages the index could not read, under the line that counts them: the count
    /// says that something was skipped, the file and the reason say what to look at.
    /// </summary>
    public static void Unreadable(MailSyncReport? report)
    {
        foreach (var line in report?.UnreadableLines() ?? [])
        {
            Say($"         {line}");
        }
    }

    /// <summary>Writes in place: the caller keeps coming back with a higher number.</summary>
    private static void Rewrite(string text)
    {
        lock (Output)
        {
            Console.Write($"\r{text}");
            _open = true;
        }
    }

    /// <summary>
    /// A line of its own. Anything already half written is ended first, so the two never overlap.
    /// </summary>
    private static void Say(string text, bool ownLine = true)
    {
        lock (Output)
        {
            if (_open)
            {
                Console.WriteLine();
                _open = false;
            }

            if (ownLine)
            {
                Console.WriteLine(text);
            }
        }
    }

    private static string Day(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(int value) => ConsoleFormat.Number(value);
}

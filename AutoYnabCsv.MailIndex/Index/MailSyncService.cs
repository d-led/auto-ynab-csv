using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace AutoYnabCsv.MailIndex.Index;

/// <summary>A message the index could not read, and what stood in the way.</summary>
public sealed record UnreadableMessage(string Path, string Reason);

public sealed record MailSyncReport(
    SyncResult Result,
    int Total,
    DateTimeOffset? IndexedFrom,
    DateTimeOffset? IndexedThrough,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset? WindowSince,
    int Partial,
    IReadOnlyList<UnreadableMessage> UnreadableMessages
)
{
    /// <summary>How many messages are named before the list only says how many more there are.</summary>
    public const int Named = 5;

    public string Describe() =>
        $"{(Result.Rebuilt ? "Rebuilt" : "Updated")}: indexed {Result.Indexed}, "
        + $"skipped {Result.Skipped}, removed {Result.Removed}, unreadable {Result.Unreadable}. "
        + Coverage;

    /// <summary>
    /// The messages the index could not read, named one by one: a count says nothing a person can
    /// act on, a file and the reason do. A long list is cut — five files explain what five hundred
    /// would, and a wall of paths only buries the one that matters.
    /// </summary>
    public IReadOnlyList<string> UnreadableLines(int named = Named)
    {
        var lines = UnreadableMessages
            .Take(named)
            .Select(message => $"{message.Reason}  {message.Path}")
            .ToList();
        var rest = UnreadableMessages.Count - lines.Count;

        if (rest > 0)
        {
            lines.Add($"and {rest} more, each with a reason of its own");
        }

        return lines;
    }

    /// <summary>What the index spans, so a gap is visible instead of silent.</summary>
    public string Coverage =>
        Total == 0
            ? "The index holds no message yet."
            : $"{Total} messages covering {Day(IndexedFrom)} … {Day(IndexedThrough)}"
                + (Partial > 0
                    ? $"; {Partial} only partly downloaded — use --search to fetch those."
                    : ".");

    private static string Day(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";
}

/// <summary>
/// The one way to bring the index up to date, shared by the CLI and the app: a routine run only
/// looks at files written since the last sync (with a week of overlap), while a first run, a
/// forced run, or changed extraction rules read everything.
/// </summary>
public static class MailSyncService
{
    public const int OverlapDays = 7;

    public static MailSyncReport Sync(
        MailIndexStore store,
        string storeRoot,
        ExtractionRuleSet rules,
        DateTimeOffset? since = null,
        bool forceFull = false,
        Action<SyncStart>? starting = null,
        Action<SyncProgress>? progress = null
    )
    {
        var rebuildNeeded = forceFull || store.Fingerprint != rules.Fingerprint;

        // A rebuild over an index that had content cleared it, so its holes are of unknown age and
        // the days since the last sync say nothing about them: the whole mailbox is walked. A first
        // run, and a routine run, stay inside the span they were asked for.
        var wholeMailbox = store.RebuildUnfinished || (rebuildNeeded && store.Count > 0);

        var window = wholeMailbox
            ? null
            : since
                ?? (store.LastSyncedAt is { } last ? last.AddDays(-OverlapDays) : null);

        starting?.Invoke(new SyncStart(rebuildNeeded, store.RebuildUnfinished, window, store.Count));

        var walk = EmlxWalker.Walk(storeRoot, window);

        // A file that vanished between the walk and the read is nothing to index: Mail moves,
        // deletes and re-downloads messages while a long walk runs. Leaving it out of the list
        // also lets its stale row be pruned below.
        var present = walk.Complete.Where(file => File.Exists(file.Path)).ToArray();
        var failures = new ConcurrentQueue<UnreadableMessage>();

        var result = MailIndexSync.Run(
            store,
            present,
            file => Read(file, rules, failures.Enqueue),
            rules.Fingerprint,
            forceFull,
            pruneMissing: window is null,
            progress: progress,
            unreadableMessage: failures.Enqueue
        );

        return new MailSyncReport(
            result,
            store.Count,
            store.IndexedFrom,
            store.IndexedThrough,
            store.LastSyncedAt,
            window,
            walk.Partial,
            failures.ToArray()
        );
    }

    /// <summary>Read one message file and reduce it to facts; null when it is not a readable message.</summary>
    public static MailFacts? Read(string path, ExtractionRuleSet rules) =>
        TryRead(
            path,
            () => ReadFacts(new EmlxFile(path, DateTimeOffset.UtcNow, 0), rules),
            blame: null
        );

    /// <summary>
    /// Reads message files and, when only headers are on this Mac, asks the mail app for the body.
    /// A message is fetched at most once per run; one that is already complete is never fetched.
    /// What a fetched body yielded goes into <paramref name="store"/>, so the next run needs no
    /// fetch — the body itself is not kept.
    /// </summary>
    public static MailLookup Lookup(
        ExtractionRuleSet rules,
        IMailBodySource? bodies,
        MailIndexStore? store = null,
        Action<MailFacts>? read = null
    )
    {
        var completed = new Dictionary<string, MailFacts?>(StringComparer.Ordinal);

        return new MailLookup(
            path => Read(path, rules),
            facts => Cached(completed, facts, rules, bodies, store, read)
        );
    }

    private static MailFacts? Cached(
        IDictionary<string, MailFacts?> completed,
        MailFacts facts,
        ExtractionRuleSet rules,
        IMailBodySource? bodies,
        MailIndexStore? store,
        Action<MailFacts>? read
    )
    {
        if (completed.TryGetValue(facts.Mail.Path, out var known))
        {
            return known;
        }

        var result = WithBody(facts, rules, bodies, out var fetched);
        completed[facts.Mail.Path] = result;

        if (fetched && result is { } extracted)
        {
            Store(store, extracted);
            read?.Invoke(extracted);
        }

        return result;
    }

    /// <summary>Keeps the extraction, not the text: the fetched body never reaches the index.</summary>
    private static void Store(MailIndexStore? store, MailFacts facts)
    {
        if (store is null)
        {
            return;
        }

        var file = new FileInfo(facts.Mail.Path);
        store.Upsert(
            new EmlxFile(facts.Mail.Path, new DateTimeOffset(file.LastWriteTimeUtc), file.Length),
            facts,
            searchBody: false
        );
    }

    private static MailFacts? WithBody(
        MailFacts? facts,
        ExtractionRuleSet rules,
        IMailBodySource? bodies,
        out bool fetched
    )
    {
        fetched = false;

        if (facts is null || facts.Mail.Text.Length > 0 || bodies is null)
        {
            return facts;
        }

        if (bodies.BodyOf(facts.Mail) is not { Length: > 0 } body)
        {
            return facts;
        }

        fetched = true;
        return SignalExtractor.Extract(facts.Mail with { Text = body }, rules);
    }

    private static MailFacts? Read(
        EmlxFile file,
        ExtractionRuleSet rules,
        Action<UnreadableMessage> blame
    ) => TryRead(file.Path, () => ReadFacts(file, rules), blame);

    private static MailFacts? TryRead(
        string path,
        Func<MailFacts?> read,
        Action<UnreadableMessage>? blame
    )
    {
        try
        {
            return read();
        }
        catch (RegexMatchTimeoutException)
        {
            Blame(blame, path, "PatternTimedOut");
            return null;
        }
        catch (FileNotFoundException)
        {
            Blame(blame, path, "Vanished");
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            Blame(blame, path, "Vanished");
            return null;
        }
        catch (IOException)
        {
            Blame(blame, path, "Unreadable");
            return null;
        }
        catch (EmlxFormatException exception)
        {
            Blame(blame, path, exception.Problem.ToString());
            return null;
        }
        catch (FormatException)
        {
            Blame(blame, path, "not a message");
            return null;
        }
    }

    private static void Blame(Action<UnreadableMessage>? blame, string path, string reason) =>
        blame?.Invoke(new UnreadableMessage(path, reason));

    private static MailFacts? ReadFacts(EmlxFile file, ExtractionRuleSet rules)
    {
        var facts = SignalExtractor.Extract(MessageParser.Parse(EmlxReader.Read(file.Path)), rules);

        return WithPlausibleDate(facts, file);
    }

    /// <summary>
    /// The date the message really has, or the file's own if the message's cannot be true.
    /// A broken import writes one absurd timestamp for a whole batch — 116 messages here share
    /// 2242-03-19 — and such a message would sit outside every search window forever.
    /// </summary>
    private static MailFacts WithPlausibleDate(MailFacts facts, EmlxFile file) =>
        Plausible(facts.Mail.ReceivedAt)
            ? facts
            : facts with { Mail = facts.Mail with { ReceivedAt = file.LastWriteUtc } };

    /// <summary>Nothing was mailed before 1990, and nothing will be mailed tomorrow.</summary>
    private static bool Plausible(DateTimeOffset when) =>
        when.Year >= 1990 && when <= DateTimeOffset.UtcNow.AddDays(1);
}

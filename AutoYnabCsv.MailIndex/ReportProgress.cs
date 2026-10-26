using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Index;

namespace AutoYnabCsv.MailIndex;

/// <summary>
/// What a report run says while it works, so a slow conversion is never a blank screen: why the
/// sync reads what it reads, how many stubs the search must read, and every entry it goes looking
/// for. A caller that only wants the result passes <see cref="Silent"/>.
/// </summary>
public sealed record ReportProgress(
    Action<SyncStart> SyncStarting,
    Action<SyncProgress> Sync,
    Action<int> Stubs,
    Action<EntrySearch> Entry
)
{
    /// <summary>How many message bodies have come back; a later call supersedes an earlier one.</summary>
    public Action<int> BodyRead { get; init; } = _ => { };

    /// <summary>How many rows the search is about to look up. Zero means none needs mail.</summary>
    public Action<int> Rows { get; init; } = _ => { };

    public static ReportProgress Silent { get; } = new(_ => { }, _ => { }, _ => { }, _ => { });
}

/// <summary>
/// What the sync is about to do, before it does it: rebuild everything because the rules changed,
/// or read only the messages written since <paramref name="Since"/>. A full walk and a windowed
/// one look identical from the outside otherwise, which makes the counters look wrong.
/// </summary>
public sealed record SyncStart(
    bool Rebuilding,
    bool Resuming,
    DateTimeOffset? Since,
    int Known,
    DateTimeOffset? Earliest = null,
    DateTimeOffset? Latest = null,
    DateTimeOffset? BuiltAt = null
);

using AutoYnabCsv.MailIndex.Correlation;

namespace AutoYnabCsv.MailIndex;

/// <summary>
/// The days an export covers. Everything the mailbox is asked about is deduced from them: a
/// conversion reads the mail that could belong to a row instead of the whole history, and it
/// loads only the facts inside that span. An export without entries has no range.
/// </summary>
public sealed record MailRange(DateOnly From, DateOnly To)
{
    /// <summary>The first moment worth reading, <paramref name="marginDays"/> before the first row.</summary>
    public DateTimeOffset Since(int marginDays) => Start(From.AddDays(-marginDays));

    /// <summary>The first moment after the span, so the last row's own day is still included.</summary>
    public DateTimeOffset Until(int marginDays) => Start(To.AddDays(marginDays + 1));

    public static MailRange? Of(IEnumerable<CorrelationEntry> entries)
    {
        var dates = entries.Select(entry => entry.Date).ToArray();

        return dates.Length == 0 ? null : new MailRange(dates.Min(), dates.Max());
    }

    /// <summary>
    /// How far back the store has to be read: never further back than the exports reach, and never
    /// later than what has changed since the last sync. The earlier of the two, so a second run
    /// over a different span still covers the older mail, and a repeated one reads nothing again.
    /// </summary>
    public static DateTimeOffset? Window(
        MailRange? range,
        DateTimeOffset? lastSyncedAt,
        int marginDays,
        int overlapDays
    ) => Earlier(range?.Since(marginDays), lastSyncedAt?.AddDays(-overlapDays));

    private static DateTimeOffset? Earlier(DateTimeOffset? first, DateTimeOffset? second) =>
        (first, second) switch
        {
            (null, null) => null,
            (null, _) => second,
            (_, null) => first,
            _ => first.Value < second.Value ? first : second,
        };

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue));
}

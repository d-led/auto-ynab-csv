using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>
/// Finds the messages the index never read: the stubs (<c>.partial.emlx</c>) Mail keeps for
/// messages whose body is still on the server. A stub has headers and nothing else, so it is
/// judged on what the headers say — the reference numbers in the subject, or the merchant the
/// transaction names — and only the ones whose day could still fit are offered. Fetching a body
/// is the expensive part, so it happens for as few messages as possible.
/// </summary>
public sealed class HeaderMailFinder(
    string root,
    Func<string, MailFacts?> read,
    Action<int>? reading = null
) : IMailFinder
{
    private IReadOnlyList<MailFacts>? _stubs;

    public IReadOnlyList<string> Find(MailQuery query, int limit = 10) =>
        Search(query, limit, Stubs());

    /// <summary>
    /// The judgement, apart from the disk: a stub fits when its day is inside the window the
    /// transaction allows and the entry's numbers or its merchant appear in the headers.
    /// </summary>
    public static IReadOnlyList<string> Search(
        MailQuery query,
        int limit,
        IEnumerable<MailFacts> stubs
    )
    {
        if (query.IsEmpty)
        {
            return [];
        }

        return stubs
            .Where(stub => Fits(query, stub))
            .OrderBy(stub => Distance(query, stub.Mail.ReceivedAt))
            .DistinctBy(stub => stub.Mail.MessageId, StringComparer.Ordinal)
            .Take(limit)
            .Select(stub => stub.Mail.Path)
            .ToArray();
    }

    private static bool Fits(MailQuery query, MailFacts stub) =>
        WithinDays(query, stub.Mail.ReceivedAt) && Mentions(query, Headers(stub));

    private static bool WithinDays(MailQuery query, DateTimeOffset received)
    {
        var day = DateOnly.FromDateTime(received.LocalDateTime);

        return day >= query.Day.AddDays(-query.WindowDays) && day <= query.Day.AddDays(query.WindowDays);
    }

    /// <summary>What a stub offers before a download: who wrote it and what it is about.</summary>
    private static string Headers(MailFacts stub) =>
        $"{stub.Mail.Subject} {stub.Mail.SenderName} {stub.Mail.SenderAddress}";

    /// <summary>
    /// A reference number is precise wherever it appears; a number that only the body holds is
    /// missed here and found by the body once it is fetched. Without a number, the merchant is
    /// the clue — and a header says "Thomann Bestellung", not "Thomann GmbH", so the longest word
    /// of the name is what has to appear.
    /// </summary>
    private static bool Mentions(MailQuery query, string headers) =>
        query.Identifiers.Any(id => headers.Contains(id, StringComparison.Ordinal))
        || (Distinctive(query.Phrase) is { } word
            && headers.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>The longest word of the clue, ignoring the legal noise around a company name.</summary>
    private static string? Distinctive(string phrase) =>
        phrase
            .Split(
                [' ', ',', '.', '-', '/', '&', '+', '(', ')'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Where(word => word.Any(char.IsLetter))
            .MaxBy(word => word.Length);

    private static int Distance(MailQuery query, DateTimeOffset received) =>
        Math.Abs(DateOnly.FromDateTime(received.LocalDateTime).DayNumber - query.Day.DayNumber);

    /// <summary>Read once per run: the same stubs answer every entry that is searched for.</summary>
    private IReadOnlyList<MailFacts> Stubs()
    {
        if (_stubs is not null)
        {
            return _stubs;
        }

        var files = EmlxWalker.Partials(root);
        reading?.Invoke(files.Count);

        _stubs = [.. files.Select(stub => read(stub.Path)).OfType<MailFacts>()];
        return _stubs;
    }
}

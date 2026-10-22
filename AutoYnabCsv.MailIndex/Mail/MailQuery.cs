namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>
/// A precise mailbox query, built from what the entry itself says: the reference numbers a
/// transfer carries, the merchant it names, and the day it happened. Numbers come first — the
/// number from the transfer is what the mail quotes back.
/// </summary>
public sealed record MailQuery(
    IReadOnlyList<string> Identifiers,
    string Phrase,
    DateOnly Day,
    int WindowDays
)
{
    public bool IsEmpty => Identifiers.Count == 0 && Phrase.Length == 0;

    /// <summary>The pattern as a person would say it: the numbers quoted, then the merchant.</summary>
    public string Describe() =>
        IsEmpty
            ? "nothing to look for"
            : string.Join(
                " + ",
                Identifiers.Select(id => $"\"{id}\"").Append(Phrase.Length > 0 ? $"\"{Phrase}\"" : string.Empty)
                    .Where(part => part.Length > 0)
            );
}

/// <summary>
/// Points at messages the index has never seen — the ones whose body Mail still holds on the
/// server. It answers with file paths, nothing else; reading and matching stay with the caller.
/// </summary>
public interface IMailFinder
{
    /// <summary>Message files that match the query, closest day first, at most a few.</summary>
    IReadOnlyList<string> Find(MailQuery query, int limit = 10);
}

using System.Text.Json;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Apply;

/// <summary>
/// One thing that happened to the plan, written down as it happened. Deleting a transaction is the
/// only action here that loses information, so the whole transaction is kept before it goes; the
/// journal is what makes "I would like that back" a command instead of a hope.
/// </summary>
public sealed record JournalEntry(
    DateTimeOffset At,
    string Kind,
    string Detail,
    string? TransactionId,
    PlannedCreate? Created,
    YnabTransaction? Deleted,
    string? MovedFromAccountId = null
)
{
    public const string CreatedKind = "created";
    public const string DeletedKind = "deleted";
    public const string CategorizedKind = "categorized";
    public const string MovedKind = "moved";
}

public static class Journal
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static void Append(string path, JournalEntry entry)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
    }

    public static IReadOnlyList<JournalEntry> Read(string path) =>
        !File.Exists(path)
            ? []
            : [.. File
                .ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<JournalEntry>(line, Options))
                .OfType<JournalEntry>()];
}

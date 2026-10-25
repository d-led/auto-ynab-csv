using System.Text;
using AutoYnabCsv.Ynab.Apply;

namespace AutoYnabCsv.Ynab.Report;

/// <summary>
/// An apply, as a count and as the newest rows it touched. The journal is the record of what was
/// done, so reading it should answer "what did I just change" without opening a line-delimited file.
/// </summary>
public static class JournalSummary
{
    public const int NewestShown = 25;

    public static string Markdown(IReadOnlyList<JournalEntry> entries, int newest = NewestShown)
    {
        if (entries.Count == 0)
        {
            return "Nothing has been applied: the journal is empty.";
        }

        var text = new StringBuilder();
        var counts = entries
            .GroupBy(entry => entry.Kind)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Count()} {group.Key}");

        text.AppendLine($"# Applied: {string.Join(", ", counts)}");
        text.AppendLine();
        text.AppendLine($"From {entries[0].At:yyyy-MM-dd HH:mm} to {entries[^1].At:yyyy-MM-dd HH:mm}.");
        text.AppendLine();

        var shown = entries.TakeLast(newest).ToList();

        text.AppendLine(
            shown.Count == entries.Count
                ? $"## Every entry ({entries.Count})"
                : $"## The newest {shown.Count} of {entries.Count}"
        );
        text.AppendLine();
        text.AppendLine("| at | kind | detail |");
        text.AppendLine("| --- | --- | --- |");

        foreach (var entry in shown)
        {
            text.AppendLine($"| {entry.At:yyyy-MM-dd HH:mm} | {entry.Kind} | {Table.Cell(entry.Detail, 90)} |");
        }

        text.AppendLine();
        text.AppendLine("`ynab undo --journal <file>` replays these backwards.");

        return text.ToString();
    }
}

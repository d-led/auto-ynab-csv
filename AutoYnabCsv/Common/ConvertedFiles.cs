namespace AutoYnabCsv.Common;

/// <summary>
/// Recognises the files this tool writes, so a second run over the same folder reads the exports
/// only and never its own output: <c>name.ynab-20261019182302.csv</c> next to the original, and
/// <c>name.ynab.csv</c> in the report's <c>ynab/</c> folder.
/// </summary>
public static class ConvertedFiles
{
    /// <summary>The marker a conversion adds before the extension.</summary>
    public const string Marker = ".ynab";

    public static bool IsConverted(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        return name.EndsWith(Marker, StringComparison.OrdinalIgnoreCase)
            || name.Contains($"{Marker}-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The export files in a folder, with what we wrote before left out.</summary>
    public static IEnumerable<string> ExportsIn(string folder) =>
        Directory
            .EnumerateFiles(folder, "*.csv", SearchOption.TopDirectoryOnly)
            .Where(path => !IsConverted(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
}

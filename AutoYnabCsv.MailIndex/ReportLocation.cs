namespace AutoYnabCsv.MailIndex;

/// <summary>
/// Where report runs go: a folder beside the exports, one timestamped run per conversion so
/// nothing collides. When the exports come from different folders the first one decides; with no
/// input at all the app data folder is used.
/// </summary>
public static class ReportLocation
{
    public const string FolderName = "auto-ynab-report";

    public static string For(IReadOnlyList<string> inputs) =>
        inputs.Count == 0
            ? AppDirectories.DefaultExportDirectory()
            : Path.Combine(DirectoryOf(inputs[0]), FolderName);

    private static string DirectoryOf(string input) =>
        Directory.Exists(input)
            ? Path.GetFullPath(input)
            : Path.GetDirectoryName(Path.GetFullPath(input)) ?? Path.GetFullPath(input);
}

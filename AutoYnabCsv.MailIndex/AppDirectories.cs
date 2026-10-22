namespace AutoYnabCsv.MailIndex;

/// <summary>Where this app keeps its data and its user-editable patterns, per operating system.</summary>
public static class AppDirectories
{
    public static string DataDirectory()
    {
        var root =
            OperatingSystem.IsMacOS() ? Path.Combine(Home(), "Library", "Application Support")
            : OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                    ?? Path.Combine(Home(), ".local", "share")
            );

        var directory = Path.Combine(root, "auto-ynab-csv");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string ConfigDirectory()
    {
        var root =
            OperatingSystem.IsMacOS() ? Path.Combine(Home(), ".config")
            : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? Path.Combine(Home(), ".config");

        var directory = Path.Combine(root, "auto-ynab-csv");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Where pattern files are looked for, farthest first: next to the executable, the user's
    /// config directory, then the nearest project folder.
    /// </summary>
    public static IReadOnlyList<string> PatternDirectories()
    {
        var directories = new List<string>
        {
            AppContext.BaseDirectory,
            ConfigDirectory(),
        };

        var project = NearestProjectDirectory();
        if (project is not null)
        {
            directories.Add(project);
        }

        return directories;
    }

    private static string? NearestProjectDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".auto-ynab-csv");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Where the searchable site goes unless <c>--out</c> says otherwise.</summary>
    public static string DefaultExportDirectory() => Path.Combine(Home(), "auto-ynab-csv-site");
}

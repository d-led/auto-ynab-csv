namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>One versioned Apple Mail data directory, such as <c>~/Library/Mail/V10</c>.</summary>
public sealed record MailStore(string Root, int Version);

public sealed class MailStoreUnavailableException(string path, Exception? inner = null)
    : Exception(
        $"The mail store at {path} cannot be read. Grant Full Disk Access to the program reading it "
            + "in System Settings → Privacy & Security → Full Disk Access, then restart it.",
        inner
    );

public static class MailStoreLocator
{
    public static string DefaultRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Mail"
        );

    /// <summary>
    /// The mail stores under <paramref name="mailRoot"/>, newest first. Empty when the directory
    /// is absent or holds no versioned stores.
    /// </summary>
    public static IReadOnlyList<MailStore> FindStores(string? mailRoot = null)
    {
        var root = mailRoot ?? DefaultRoot();
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(root)
            .Select(path => new MailStore(path, VersionOf(Path.GetFileName(path))))
            .Where(store => store.Version > 0)
            .OrderByDescending(store => store.Version)
            .ToArray();
    }

    /// <summary>The stores below <paramref name="mailRoot"/>; a denied directory becomes the documented error.</summary>
    public static IReadOnlyList<MailStore> FindReadableStores(string? mailRoot = null)
    {
        var root = mailRoot ?? DefaultRoot();
        if (!Directory.Exists(root))
        {
            return [];
        }

        EnsureReadable(root);
        return FindStores(root);
    }

    public static void EnsureReadable(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            _ = entries.MoveNext();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new MailStoreUnavailableException(path, exception);
        }
    }

    private static int VersionOf(string directoryName) =>
        directoryName.Length > 1
        && char.ToUpperInvariant(directoryName[0]) == 'V'
        && int.TryParse(directoryName.AsSpan(1), out var version)
            ? version
            : -1;
}

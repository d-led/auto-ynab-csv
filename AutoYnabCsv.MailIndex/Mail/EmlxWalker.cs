namespace AutoYnabCsv.MailIndex.Mail;

public sealed record EmlxFile(string Path, DateTimeOffset LastWriteUtc, long Length);

/// <summary>What one walk of the store found, split by whether the body is on this Mac.</summary>
public sealed record EmlxWalk(IReadOnlyList<EmlxFile> Complete, int Partial);

public static class EmlxWalker
{
    /// <summary>
    /// The stubs Mail leaves for messages whose body is still on the server: headers and no body,
    /// and the only local trace of a message the index has never read.
    /// </summary>
    public static IReadOnlyList<EmlxFile> Partials(string root) =>
        MessageFiles(root)
            .Where(IsPartial)
            .Select(Describe)
            .ToArray();

    /// <summary>
    /// Every complete <c>.emlx</c> file below <paramref name="root"/>. Partly downloaded messages
    /// (<c>.partial.emlx</c>) carry headers but no body: they are counted here, searched by
    /// <see cref="HeaderMailFinder"/>, and never parsed, because Mail has to hand over a body that
    /// is not on this Mac.
    /// </summary>
    public static EmlxWalk Walk(string root, DateTimeOffset? changedSince = null)
    {
        var complete = new List<EmlxFile>();
        var partial = 0;

        foreach (var path in MessageFiles(root))
        {
            if (IsPartial(path))
            {
                partial++;
                continue;
            }

            if (changedSince is { } since && File.GetLastWriteTimeUtc(path) <= since.UtcDateTime)
            {
                continue;
            }

            complete.Add(Describe(path));
        }

        return new EmlxWalk(complete, partial);
    }

    private static IEnumerable<string> MessageFiles(string root) =>
        Directory.EnumerateFiles(root, "*.emlx", Options);

    private static bool IsPartial(string path) =>
        path.EndsWith(".partial.emlx", StringComparison.OrdinalIgnoreCase);

    private static EmlxFile Describe(string path)
    {
        var info = new FileInfo(path);
        return new EmlxFile(path, new DateTimeOffset(info.LastWriteTimeUtc), info.Length);
    }

    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };
}

using System.Globalization;
using System.Reflection;

namespace AutoYnabCsv.Common;

/// <summary>
/// When this build was made, so a binary that is older than the code says so instead of being
/// mistaken for the one just built. The time comes from the file the process runs from: for a
/// single-file publish that is the program itself, and for <c>dotnet run</c> it is the assembly,
/// because there the process is the host and its timestamp belongs to the SDK.
/// </summary>
public static class BuildStamp
{
    /// <summary>The version and when it was built, as one line.</summary>
    public static string Line(string version, DateTimeOffset? built) =>
        built is { } moment
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{version} (built {moment.UtcDateTime:yyyy-MM-dd HH:mm} UTC)"
            )
            : $"{version} (build time unknown)";

    /// <summary>When the file this process runs from was last written, if it can be found.</summary>
    public static DateTimeOffset? Of() => WrittenAt(Location());

    /// <summary>
    /// The file this process runs from: the program itself once published as one file, and the assembly
    /// under <c>dotnet run</c>, where the process is the host and its timestamp belongs to the SDK.
    /// </summary>
    public static string? Location() =>
        Assembly.GetEntryAssembly()?.Location is { Length: > 0 } assembly
            ? assembly
            : Environment.ProcessPath;

    /// <summary>When a file was written, or nothing when there is no such file to ask.</summary>
    public static DateTimeOffset? WrittenAt(string? path) =>
        path is not null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
}

using System.Globalization;

namespace AutoYnabCsv.App;

/// <summary>Numbers and sizes as the console says them, in one place so every line agrees.</summary>
internal static class ConsoleFormat
{
    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>How long a step took, said the way a person waits: seconds, then minutes.</summary>
    public static string Duration(long milliseconds) =>
        milliseconds switch
        {
            < 1000 => "<1s",
            < 60_000 => $"{milliseconds / 1000}s",
            _ => $"{milliseconds / 60_000}m {milliseconds / 1000 % 60}s",
        };

    /// <summary>Bytes as a person reads them; nobody needs the exact number for a cache.</summary>
    public static string Size(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.0} kB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
        };
}

using System.Text.RegularExpressions;

namespace AutoYnabCsv.Ynab;

/// <summary>
/// The access token is read from the environment and never printed, but it still must not arrive in
/// a log by accident — an API error echoes the request, a library formats the headers, a stack trace
/// quotes a message. Everything this tool prints about YNAB goes through here first.
/// </summary>
public static partial class Secrets
{
    public const string Mask = "***";

    public static string Redact(string? text, string? secret)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var withoutSecret = string.IsNullOrEmpty(secret) ? text : text.Replace(secret, Mask, StringComparison.Ordinal);

        return BearerHeader().Replace(withoutSecret, $"Bearer {Mask}");
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerHeader();
}

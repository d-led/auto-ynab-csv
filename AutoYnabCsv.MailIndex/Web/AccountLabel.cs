using System.Text.RegularExpressions;

namespace AutoYnabCsv.MailIndex.Web;

/// <summary>
/// A short, readable name for an exported account, derived only from its file name:
/// the date prefix and "Umsatzliste" go, a trailing account number stays as four digits.
/// </summary>
public static partial class AccountLabel
{
    public static string Of(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);

        foreach (var prefix in new[] { @"^\d{2}-\d{2}-\d{4}[_-]", "^Umsatzliste[_-]" })
        {
            stem = Regex.Replace(stem, prefix, string.Empty);
        }

        if (IbanSuffix().Match(stem) is { Success: true } iban)
        {
            var name = Humanize(stem[..iban.Index]);
            var digits = Digits().Match(iban.Value).Groups[1].Value;
            return digits.Length >= 4 ? $"{name} · {digits[^4..]}" : name;
        }

        if (TrailingNumber().Match(stem) is { Success: true } tail)
        {
            var name = Humanize(stem[..tail.Index]);
            return $"{name} · {tail.Groups[1].Value}";
        }

        return stem.StartsWith("n26", StringComparison.OrdinalIgnoreCase)
            ? "n26"
            : Humanize(stem);
    }

    private static string Humanize(string value) =>
        Whitespace().Replace(value.Replace('_', ' '), " ").Trim();

    [GeneratedRegex(@"_?DE\d{18,22}(?:[-_][\w-]+)?$")]
    private static partial Regex IbanSuffix();

    [GeneratedRegex(@"DE(\d{18,22})")]
    private static partial Regex Digits();

    [GeneratedRegex(@"[-_](\d{3,4})$")]
    private static partial Regex TrailingNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

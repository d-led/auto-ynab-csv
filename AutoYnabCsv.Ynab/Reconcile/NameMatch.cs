using System.Globalization;
using System.Text;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>
/// How alike two descriptions are, in a way a person would agree with: the bank writes
/// <c>PAYPAL EUROPE S.A.R.L.</c> where the plan says <c>PayPal</c>, and both mean the same shop.
/// Case, punctuation and accents are noise; the words are the signal.
/// </summary>
public static class NameMatch
{
    private static readonly char[] Noise = [.. ",.;:/\\-_|()[]{}*#+&\"'`~!?<>="];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutAccents = new string(
            [.. value.Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)]
        );

        var words = withoutAccents
            .ToLowerInvariant()
            .Split(Noise, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim())
            .Where(word => word.Length > 0);

        return string.Join(' ', words);
    }

    /// <summary>1 for the same words, 0 for nothing in common; a substring counts for a lot.</summary>
    public static double Similarity(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return 0.75;
        }

        var leftWords = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightWords = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var shared = leftWords.Intersect(rightWords, StringComparer.Ordinal).Count();

        return shared == 0 ? 0 : (double)shared / leftWords.Union(rightWords, StringComparer.Ordinal).Count();
    }
}

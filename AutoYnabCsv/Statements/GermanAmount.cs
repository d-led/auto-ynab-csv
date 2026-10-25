using System.Globalization;

namespace AutoYnabCsv.Statements;

/// <summary>
/// Money as the German exports write it: thousands separated by a dot, decimals by a comma, and
/// the currency trailing behind. DKB also writes a doubled minus in front of a credit card saldo
/// (<c>--87,58 EUR</c>), and a raw decimal with float noise that is meant to be two places
/// (<c>--353,66999999999996</c>); both mean one negative amount.
/// </summary>
public static class GermanAmount
{
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0m;
        var digits = Only(t => char.IsDigit(t) || t is ',' or '.' or '-', text);

        if (digits.Trim('-').Length == 0)
        {
            return false;
        }

        var isNegative = digits.StartsWith('-');
        var normalized = digits.TrimStart('-').Replace(".", string.Empty).Replace(',', '.');

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        amount = Math.Round(isNegative ? -value : value, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    private static string Only(Func<char, bool> keep, string? text) =>
        text is null ? string.Empty : new string([.. text.Where(keep)]);
}

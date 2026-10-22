using System.Globalization;

namespace AutoYnabCsv.MailIndex.Facts;

/// <summary>Reads the amounts that mail and bank exports write, whether <c>1.234,56</c> or <c>1,234.56</c>.</summary>
public static class AmountParser
{
    public static bool TryParse(string text, out decimal amount)
    {
        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');

        var normalized = (lastComma, lastDot) switch
        {
            ( >= 0, >= 0) => NormalizeWithBothSeparators(text, lastComma, lastDot),
            ( >= 0, _) => text.Replace(',', '.'),
            (_, >= 0) => text,
            _ => text,
        };

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out amount
        );
    }

    private static string NormalizeWithBothSeparators(string text, int lastComma, int lastDot)
    {
        var decimalSeparator = lastComma > lastDot ? ',' : '.';
        var groupSeparator = decimalSeparator == ',' ? '.' : ',';

        return text.Replace(groupSeparator.ToString(), string.Empty).Replace(decimalSeparator, '.');
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using AutoYnabCsv.Contracts;

namespace AutoYnabCsv.Statements;

/// <summary>
/// Reads the header lines the export detectors only look for. The lines are found by their own
/// label and not by their position, so a bank adding a line does not silently shift the balance
/// into a wrong field: <c>"Kontostand vom 31.12.2024:";"1.234,50 €"</c> and
/// <c>"Saldo vom 02.01.2025:";"--123,45 EUR"</c>.
/// </summary>
public static partial class ExportStatementReader
{
    public static ExportStatement Read(string input, string sourceType) =>
        sourceType switch
        {
            KnownSources.DkbGiroSource => ReadGiro(Lines(input)),
            KnownSources.DkbVisaSource => ReadVisa(Lines(input)),
            _ => ExportStatement.Unknown,
        };

    private static ExportStatement ReadGiro(IReadOnlyList<string> lines)
    {
        var account = Fields(lines.FirstOrDefault() ?? string.Empty);
        var period = PeriodOf(lines.FirstOrDefault(line => line.StartsWith("\"Zeitraum", StringComparison.OrdinalIgnoreCase)));
        var balance = lines.FirstOrDefault(line => line.StartsWith("\"Kontostand", StringComparison.OrdinalIgnoreCase));

        return new ExportStatement(
            AccountName: account.ElementAtOrDefault(0) ?? string.Empty,
            AccountIdentifier: account.ElementAtOrDefault(1) ?? string.Empty,
            PeriodFrom: period.From,
            PeriodTo: period.To,
            BalanceDate: DayOf(balance),
            ClosingBalance: AmountOf(balance)
        );
    }

    private static ExportStatement ReadVisa(IReadOnlyList<string> lines)
    {
        var card = Fields(lines.FirstOrDefault() ?? string.Empty);
        var balance = lines.FirstOrDefault(line => line.StartsWith("\"Saldo", StringComparison.OrdinalIgnoreCase));

        return new ExportStatement(
            AccountName: card.ElementAtOrDefault(1) ?? string.Empty,
            AccountIdentifier: card.ElementAtOrDefault(2) ?? string.Empty,
            PeriodFrom: null,
            PeriodTo: null,
            BalanceDate: DayOf(balance),
            ClosingBalance: AmountOf(balance)
        );
    }

    private static (DateOnly? From, DateOnly? To) PeriodOf(string? line) =>
        line is not null && Range().Match(line) is { Success: true } range
            ? (ParseDay(range.Groups[1].Value), ParseDay(range.Groups[2].Value))
            : (null, null);

    private static DateOnly? DayOf(string? line) =>
        line is not null && SingleDay().Match(line) is { Success: true } day
            ? ParseDay(day.Groups[1].Value)
            : null;

    private static decimal? AmountOf(string? line)
    {
        var amount = Fields(line ?? string.Empty).ElementAtOrDefault(1);
        return GermanAmount.TryParse(amount, out var value) ? value : null;
    }

    private static DateOnly? ParseDay(string text) =>
        DateOnly.TryParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    private static IReadOnlyList<string> Lines(string input) =>
        [.. input.Split('\n').Select(line => line.Trim('\r', ' ', '\uFEFF')).Where(line => line.Length > 0)];

    /// <summary>The exports quote every field, so the separator is a quoted semicolon.</summary>
    private static IReadOnlyList<string> Fields(string line) =>
        [.. line.Split("\";\"").Select(field => field.Trim().Trim('"'))];

    [GeneratedRegex(@"(\d{2}\.\d{2}\.\d{4})\s*-\s*(\d{2}\.\d{2}\.\d{4})")]
    private static partial Regex Range();

    [GeneratedRegex(@"(\d{2}\.\d{2}\.\d{4})")]
    private static partial Regex SingleDay();
}

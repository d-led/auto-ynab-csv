using AutoYnabCsv.Contracts;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Detectors;
using AutoYnabCsv.Statements;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>
/// Turns the files a bank exports into statements the reconciliation can compare: the rows in the
/// currency the bank used, the account label the transfer naming already relies on, and the balance
/// the header declares. Detection is the converter's own, so an export that converts is exactly the
/// export that reconciles.
/// </summary>
public static class BankStatementReader
{
    /// <summary>Files this tool wrote itself are never mistaken for an export on a second run.</summary>
    public static IReadOnlyList<BankStatement> ReadFolder(string folder) =>
        [.. Directory
            .EnumerateFiles(folder, "*.csv", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Where(path => !IsOurOutput(path))
            .Select(TryRead)
            .OfType<BankStatement>()];

    public static BankStatement? TryRead(string path)
    {
        var text = File.ReadAllText(path);
        var detection = DetectFirst.Instance.TryDetect(text);

        return detection.Successful ? Read(path, text, detection.SourceType) : null;
    }

    public static BankStatement Read(string path, string text, string sourceType) =>
        new(
            SourceFile: path,
            Label: AccountLabel.Of(path),
            Statement: ExportStatementReader.Read(text, sourceType),
            Rows: [.. DetectAndConvert.Instance
                .Convert(text)
                .Select((entry, index) => new BankRow(
                    Date: entry.Date,
                    Amount: entry.Inflow - entry.Outflow,
                    Payee: entry.Payee.Trim(),
                    Memo: entry.Memo.Trim(),
                    SourceFile: path,
                    Index: index,
                    CounterpartyIban: entry.CounterpartyIban.Trim()))
                .OrderBy(row => row.Date)
                .ThenBy(row => row.Index)]
        );

    private static bool IsOurOutput(string path) =>
        Path.GetFileName(path).Contains(".ynab-", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(path).Contains(".ynab.", StringComparison.OrdinalIgnoreCase);
}

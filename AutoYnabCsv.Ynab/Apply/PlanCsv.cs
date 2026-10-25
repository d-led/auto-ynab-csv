using AutoYnabCsv.Contracts;
using AutoYnabCsv.Exporters;

namespace AutoYnabCsv.Ynab.Apply;

/// <summary>
/// The rows a plan wants added, written as the files YNAB imports: one per account, in the shape the
/// app reads. Importing costs nothing from the hourly allowance, which is why rows are added this way
/// and the API is left for what a file cannot do — removing, moving and categorising.
/// </summary>
public static class PlanCsv
{
    /// <summary>Writes one file per account and answers with the paths, so the caller can name them.</summary>
    public static IReadOnlyList<string> Write(RepairPlan plan, string folder)
    {
        Directory.CreateDirectory(folder);

        return
        [
            .. plan
                .Creates.GroupBy(create => create.AccountName, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    var file = Path.Combine(folder, $"{Safe(group.Key)}.csv");
                    File.WriteAllText(file, YnabCsvExporter.Export(group.Select(AsRow)));
                    return file;
                })
        ];
    }

    private static YnabImportEntry AsRow(PlannedCreate create) =>
        new(
            Date: create.Date,
            Payee: create.PayeeName,
            Memo: create.Memo,
            Inflow: create.Amount.IsNegative ? 0m : create.Amount.Amount,
            Outflow: create.Amount.IsNegative ? -create.Amount.Amount : 0m
        );

    /// <summary>An account name may hold a middle dot or a slash; a file name may not.</summary>
    private static string Safe(string account) =>
        string.Concat(
            account.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) || character == ' ' ? '-' : character));
}

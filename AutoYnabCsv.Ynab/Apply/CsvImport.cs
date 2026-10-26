using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Apply;

/// <summary>One row of a file written for importing, as the file says it.</summary>
public sealed record ImportRow(DateOnly Date, string Payee, string Memo, Money Amount);

/// <summary>
/// A file of rows for one account. The account is the file's name — YNAB asks for the account when a
/// file is uploaded, so a file cannot carry it — which is exactly why the choice is shown and
/// confirmed before anything is created.
/// </summary>
public sealed record ImportFile(string Path, string AccountName, IReadOnlyList<ImportRow> Rows)
{
    public Money Total => Money.Sum(Rows.Select(row => row.Amount));

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? ".";
}

/// <summary>
/// The files a plan wrote, read back so they can be imported through the API with the account chosen
/// here rather than in a web form. Every row keeps a readable, stable <c>import_id</c>, so importing
/// the same file twice does not double the account: YNAB recognises the row it already has.
/// </summary>
public static class CsvImport
{
    /// <summary>YNAB refuses a longer one; this leaves room and stays readable.</summary>
    public const int MaxImportIdLength = 36;

    /// <summary>Reads every CSV in a folder as one file of rows per account.</summary>
    public static IReadOnlyList<ImportFile> Read(string folder) =>
        Directory
            .EnumerateFiles(folder, "*.csv")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(ReadOne)
            .Where(file => file.Rows.Count > 0)
            .ToArray();

    /// <summary>
    /// Which account the files are for, by name. A name the plan does not know is not guessed at: the
    /// caller shows the list and asks, because a row in the wrong account is money in the wrong place.
    /// </summary>
    public static IReadOnlyList<string> AccountsNamed(IEnumerable<ImportFile> files) =>
        files.Select(file => file.AccountName).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The rows of the chosen files as a plan of creates, so the applier and the journal carry them.</summary>
    public static RepairPlan Plan(
        string planId,
        IReadOnlyDictionary<string, string> accountIds,
        IEnumerable<ImportFile> files
    ) =>
        new(
            PlanId: planId,
            CreatedAt: DateTimeOffset.UtcNow,
            Creates:
            [
                .. files.SelectMany(file =>
                    file.Rows.Select(row =>
                        new PlannedCreate(
                            AccountId: accountIds[file.AccountName],
                            AccountName: file.AccountName,
                            Date: row.Date,
                            Amount: row.Amount,
                            PayeeName: row.Payee,
                            Memo: row.Memo,
                            ImportId: ImportIdOf(file.AccountName, row),
                            CategoryId: null
                        )
                    )
                ),
            ],
            Deletes: [],
            Categories: [],
            Notes: [],
            Moves: []
        );

    /// <summary>
    /// The same row always gets the same id, and the id is the row's own fingerprint rather than a
    /// counter: a second run of the same file is a no-op instead of a duplicate.
    /// </summary>
    public static string ImportIdOf(string account, ImportRow row)
    {
        var text = string.Join(
            '|',
            account,
            row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.Amount.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            row.Payee,
            row.Memo
        );
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        return $"csv-{hash[..(MaxImportIdLength - 4)].ToLowerInvariant()}";
    }

    /// <summary>The command the choices amount to, to write down instead of answering the questions twice.</summary>
    public static string CommandFor(
        IReadOnlyDictionary<string, string> accountIds,
        IEnumerable<ImportFile> files,
        string folder
    ) =>
        "auto-ynab-csv ynab import"
        + $" --csv {Quoted(folder)}"
        + string.Concat(
            files.Select(file =>
                $" --account {Quoted(file.AccountName)}={accountIds[file.AccountName]}"
            )
        )
        + " --apply --yes";

    private static string Quoted(string text) =>
        text.Contains(' ') ? $"\"{text}\"" : text;

    private static ImportFile ReadOne(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var rows = new List<ImportRow>();

        // The header is the exporter's own; being tolerant about its case costs nothing and saves a
        // failure when the file was touched by a spreadsheet.
        var columns = Header(lines.FirstOrDefault());
        foreach (var line in lines.Skip(1).Where(line => line.Trim().Length > 0))
        {
            var cells = line.Split(',');
            if (cells.Length < columns.Count)
            {
                continue;
            }

            var inflow = Amount(cells[columns["inflow"]]);
            var outflow = Amount(cells[columns["outflow"]]);
            var amount = inflow != 0m ? inflow : -outflow;

            rows.Add(
                new ImportRow(
                    DateOnly.ParseExact(
                        cells[columns["date"]].Trim(),
                        ["yyyy-MM-dd", "dd.MM.yyyy"],
                        CultureInfo.InvariantCulture
                    ),
                    cells[columns["payee"]].Trim(),
                    cells[columns["memo"]].Trim(),
                    amount
                )
            );
        }

        return new ImportFile(path, AccountOf(path), rows);
    }

    /// <summary>The file is named after its account, which is how a plan writes it.</summary>
    private static string AccountOf(string path) =>
        Path.GetFileNameWithoutExtension(path).Replace('-', ' ');

    private static IReadOnlyDictionary<string, int> Header(string? line)
    {
        var names = (line ?? "Date,Payee,Memo,Inflow,Outflow")
            .Split(',')
            .Select((name, index) => (Name: name.Trim().ToLowerInvariant(), Index: index))
            .GroupBy(cell => cell.Name)
            .ToDictionary(group => group.Key, group => group.First().Index);

        foreach (var needed in (string[])["date", "payee", "memo", "inflow", "outflow"])
        {
            names.TryAdd(needed, Array.IndexOf(["date", "payee", "memo", "inflow", "outflow"], needed));
        }

        return names;
    }

    private static decimal Amount(string cell) =>
        decimal.TryParse(
            cell.Trim().Replace(" ", string.Empty),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var amount
        )
            ? amount
            : 0m;
}

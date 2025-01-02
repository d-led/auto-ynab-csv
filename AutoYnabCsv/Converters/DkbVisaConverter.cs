using System.Globalization;
using AutoYnabCsv.Contracts;
using CsvHelper;
using CsvHelper.Configuration;

namespace AutoYnabCsv.Converters;

public class DkbVisaConverter : IConvertInput
{
    public IEnumerable<YnabImportEntry> Convert(string input)
    {
        var filteredText = string.Join("\n", input
            .Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(4)
        );
        using var reader = new StringReader(filteredText);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            BadDataFound = args =>
            {
                Console.WriteLine($"Bad data found on row {args.Context.Parser?.Count}: {args.RawRecord}");
            },
            MissingFieldFound = null
        };
        using var csv = new CsvReader(reader, config);
        return
            csv.GetRecords<DkbVisaExportEntry>()
                .ToList()
                .Select(entry => new YnabImportEntry(
                    Date: entry.BookingDate,
                    Payee: entry.Description,
                    Memo: entry.TransactionType,
                    Inflow: entry.Amount > 0 ? Math.Abs(entry.Amount) : 0,
                    Outflow: entry.Amount < 0 ? Math.Abs(entry.Amount) : 0))
            ;
    }
}

using System.Globalization;
using AutoYnabCsv.Contracts;
using CsvHelper;
using CsvHelper.Configuration;

namespace AutoYnabCsv.Converters;

public class DkbGiroConverter : IConvertInput
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
            MissingFieldFound = null // Ignore missing fields
        };
        using var csv = new CsvReader(reader, config);
        return
            csv.GetRecords<DkbGiroExportEntry>()
                .ToList()
                .Select(entry => new YnabImportEntry(
                    Date: entry.BookingDate,
                    Payee: entry.Amount > 0 ? entry.Payer : entry.Payee,
                    Memo: entry.Purpose,
                    Inflow: entry.Amount > 0 ? Math.Abs(entry.Amount) : 0,
                    Outflow: entry.Amount < 0 ? Math.Abs(entry.Amount) : 0))
            ;
    }
}

using System.Globalization;
using AutoYnabCsv.Contracts;
using CsvHelper;
using CsvHelper.Configuration;

namespace AutoYnabCsv.Converters;

public class DkbGiroConverter(OwnAccountBook? own = null) : IConvertInput
{
    private readonly OwnAccountBook _own = own ?? OwnAccountBook.None;

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
            csv.GetRecords<DkbGiroExportEntry>()
                .ToList()
                .Select(entry => new YnabImportEntry(
                    Date: entry.BookingDate,
                    Payee: TransferNaming.Payee(entry.Amount > 0 ? entry.Payer : entry.Payee, entry.Iban, _own),
                    Memo: TransferNaming.Memo(entry.Purpose, entry.Iban, _own),
                    Inflow: entry.Amount > 0 ? Math.Abs(entry.Amount) : 0,
                    Outflow: entry.Amount < 0 ? Math.Abs(entry.Amount) : 0,
                    CounterpartyIban: entry.Iban))
            ;
    }
}

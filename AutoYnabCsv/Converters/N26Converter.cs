using System.Globalization;
using AutoYnabCsv.Contracts;
using CsvHelper;

namespace AutoYnabCsv.Converters;

public class N26Converter(OwnAccountBook? own = null) : IConvertInput
{
    private readonly OwnAccountBook _own = own ?? OwnAccountBook.None;

    public IEnumerable<YnabImportEntry> Convert(string input)
    {
        using var reader = new StringReader(input);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return
            csv.GetRecords<N26ExportEntry>()
                .ToList()
                .Select(entry => new YnabImportEntry(
                    Date: entry.ValueDate,
                    Payee: TransferNaming.Payee(entry.PartnerName, entry.PartnerIban, _own),
                    Memo: TransferNaming.Memo(entry.PaymentReference, entry.PartnerIban, _own),
                    Inflow: entry.Amount > 0 ? Math.Abs(entry.Amount) : 0,
                    Outflow: entry.Amount < 0 ? Math.Abs(entry.Amount) : 0,
                    CounterpartyIban: entry.PartnerIban))
            ;
    }
}
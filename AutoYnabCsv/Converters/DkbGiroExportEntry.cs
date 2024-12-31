using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using CsvHelper.TypeConversion;

namespace AutoYnabCsv.Converters;

public record DkbGiroExportEntry
{
    [Name("Buchungsdatum")]
    [Format("dd.MM.yy")]
    public DateOnly BookingDate { get; set; }

    [Name("Wertstellung")]
    [Format("dd.MM.yy")]
    public DateOnly ValueDate { get; set; }

    [Name("Status")] public string Status { get; set; } = string.Empty;

    [Name("Zahlungspflichtige*r")] public string Payer { get; set; } = string.Empty;

    [Name("Zahlungsempfänger*in")] public string Payee { get; set; } = string.Empty;

    [Name("Verwendungszweck")] public string Purpose { get; set; } = string.Empty;

    [Name("Umsatztyp")] public string TransactionType { get; set; } = string.Empty;

    [Name("IBAN")] public string Iban { get; set; } = string.Empty;

    [Name("Betrag (€)")]
    [TypeConverter(typeof(EuropeanDecimalConverter))]
    public decimal Amount { get; set; }

    [Name("Gläubiger-ID")] public string CreditorId { get; set; } = string.Empty;

    [Name("Mandatsreferenz")] public string MandateReference { get; set; } = string.Empty;

    [Name("Kundenreferenz")] public string CustomerReference { get; set; } = string.Empty;
}

public class EuropeanDecimalConverter : DefaultTypeConverter
{
    public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0m;
        }

        text = text
            .Replace(".", "")
            .Replace(",", ".");

        if (decimal.TryParse(
                text,
                out var result)
           )
        {
            return result;
        }

        // throw new TypeConverterException(this, memberMapData, text, row.Context, $"Cannot convert '{text}' to decimal.");
        return 0m;
    }
}
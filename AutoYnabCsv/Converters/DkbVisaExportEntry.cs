using CsvHelper.Configuration.Attributes;

namespace AutoYnabCsv.Converters;

public class DkbVisaExportEntry
{
    [Name("Belegdatum")]
    [Format("dd.MM.yy")]
    public DateOnly BookingDate { get; set; }
        
    [Name("Wertstellung")]
    [Format("dd.MM.yy")]
    public DateOnly ValueDate { get; set; }
        
    [Name("Status")]
    public string Status { get; set; } = string.Empty;
        
    [Name("Beschreibung")]
    public string Description { get; set; } = string.Empty;
        
    [Name("Umsatztyp")]
    public string TransactionType { get; set; } = string.Empty;
        
    [Name("Betrag (€)")]
    [TypeConverter(typeof(EuropeanDecimalConverter))]
    public decimal Amount { get; set; }
        
    [Name("Fremdwährungsbetrag")]
    [TypeConverter(typeof(EuropeanDecimalConverter))]
    public decimal ForeignCurrencyAmount { get; set; }
}
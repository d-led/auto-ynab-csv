using CsvHelper.Configuration.Attributes;

namespace AutoYnabCsv.Converters;

public record N26ExportEntry
{
    [Name("Booking Date")]
    [Format("yyyy-MM-dd")]
    public DateOnly BookingDate { get; set; }

    [Name("Value Date")]
    [Format("yyyy-MM-dd")]
    public DateOnly ValueDate { get; set; }

    [Name("Partner Name")]
    public string PartnerName { get; set; } = string.Empty;

    [Name("Partner Iban")]
    public string PartnerIban { get; set; } = string.Empty;

    [Name("Type")]
    public string Type { get; set; } = string.Empty;

    [Name("Payment Reference")]
    public string PaymentReference { get; set; } = string.Empty;

    [Name("Account Name")]
    public string AccountName { get; set; } = string.Empty;

    [Index(7)]
    [Default("0")]
    public decimal Amount { get; set; }

    [Name("Original Amount")]
    [Default("0")]
    public decimal OriginalAmount { get; set; }

    [Name("Original Currency")]
    public string OriginalCurrency { get; set; } = string.Empty;

    [Name("Exchange Rate")]
    [Default("0")]
    public decimal ExchangeRate { get; set; }
}

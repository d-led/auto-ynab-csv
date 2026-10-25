using CsvHelper.Configuration.Attributes;

namespace AutoYnabCsv.Contracts;

[Delimiter(",")]
public record YnabImportEntry(
    DateOnly Date,
    string Payee,
    string Memo,
    decimal Inflow,
    decimal Outflow,
    string CounterpartyIban = ""
    )
{
    /*
     * "Date","Payee","Memo","Outflow","Inflow"
     */
    [Name("Date")]
    [Format("yyyy-MM-dd")]
    public DateOnly Date { get; set; } = Date;

    [Name("Payee")]
    public string Payee { get; set; } = Payee;

    [Name("Memo")]
    public string Memo { get; set; } = Memo;

    [Name("Inflow")]
    public decimal Inflow { get; set; } = Inflow;

    [Name("Outflow")]
    public decimal Outflow { get; set; } = Outflow;

    /// <summary>
    /// The other side's account number where the export prints one. It is what makes a movement
    /// between two of your own accounts a transfer rather than a payment to a person. It is not part
    /// of a YNAB row, so it is never written to the CSV.
    /// </summary>
    [Ignore]
    public string CounterpartyIban { get; set; } = CounterpartyIban;
}

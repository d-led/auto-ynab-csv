using CsvHelper.Configuration.Attributes;

namespace AutoYnabCsv.Contracts;

[Delimiter(",")]
public record YnabImportEntry(
    DateOnly Date,
    string Payee,
    string Memo,
    decimal Inflow,
    decimal Outflow
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
}

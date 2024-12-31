using ApprovalTests;
using ApprovalTests.Reporters;
using AutoYnabCsv.Contracts;
using AutoYnabCsv.Exporters;

namespace Tests;

[TestClass]
[UseReporter(typeof(DiffReporter))]
public class YnabCsvExporterTest
{
    [TestMethod]
    public void ExportingEmptyCsv()
    {
        Approvals.Verify(YnabCsvExporter.Export([]));
    }

    [TestMethod]
    public void ExportingNonEmptyCsv()
    {
        Approvals.Verify(YnabCsvExporter.Export([
            new YnabImportEntry(
                Date: new DateOnly(2024, 12, 22), 
                Payee: "Some payee",
                 Memo: "memo1",
                 Inflow: 0,
                Outflow: 2.5m),
            new YnabImportEntry(
                Date: new DateOnly(2024, 1, 23), 
                Payee: "Payee2",
                Memo: "memo with comma, here",
                Inflow: 3500.0m,
                Outflow: 0),
        ]));
    }
}
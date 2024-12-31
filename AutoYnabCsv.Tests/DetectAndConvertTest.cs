using ApprovalTests;
using ApprovalTests.Reporters;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Exporters;

namespace Tests;

[TestClass]
[UseReporter(typeof(DiffReporter))]
public class DetectAndConvertTest
{
    [TestMethod]
    [DataRow("n26-download.csv")]
    public void DetectKnownCsv(string inputFilename)
    {
        var input = TestHelpers.SampleTextOf(inputFilename);
        var conversion = DetectAndConvert.Instance.Convert(input);
        var csv = YnabCsvExporter.Export(conversion);
        Approvals.Verify(csv);
    }
}
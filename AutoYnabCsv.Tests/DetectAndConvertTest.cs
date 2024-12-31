using System.Globalization;
using ApprovalTests;
using ApprovalTests.Namers;
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
    [DataRow("dkb-giro.csv")]
    public void DetectAndConvertKnownCsv(string inputFilename)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture; 
        var input = TestHelpers.SampleTextOf(inputFilename);
        var conversion = DetectAndConvert.Instance.Convert(input);
        var csv = YnabCsvExporter.Export(conversion);
        using (ApprovalResults.ForScenario(inputFilename))
        {
            Approvals.Verify(csv);
        } 
    }
}
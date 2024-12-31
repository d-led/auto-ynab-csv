using ApprovalTests;
using ApprovalTests.Core.Exceptions;
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
        var input = TestHelpers.SampleTextOf(inputFilename);
        var conversion = DetectAndConvert.Instance.Convert(input);
        var csv = YnabCsvExporter.Export(conversion);
        using (ApprovalResults.ForScenario(inputFilename))
        {
            try
            {
                Approvals.Verify(csv);
            } catch (ApprovalMismatchException ex)
            {
                Console.WriteLine("Approval mismatch:");
                Console.WriteLine(ex.Message);
                Console.WriteLine("Expected:");
                Console.WriteLine(ex.Received);
                Console.WriteLine("Actual:");
                Console.WriteLine(ex.Approved);
                throw;
            }
        } 
    }
}
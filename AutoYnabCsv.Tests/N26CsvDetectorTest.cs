using AutoYnabCsv.Contracts;
using AutoYnabCsv.Detectors;

namespace Tests;

[TestClass]
public sealed class N26CsvDetectorTest
{
    const string SamplesDir = "../../../../data/samples/";

    [TestMethod]
    public void DetectN26Csv()
    {
        var input = SampleTextOf("n26-download.csv");
        Assert.AreEqual(KnownSources.N26, new N26CsvDetector().TryDetect(input));
    }

    private static string SampleTextOf(string sample)
    {
        var input = File.ReadAllText(Path.Join(SamplesDir, sample));
        return input;
    }
}

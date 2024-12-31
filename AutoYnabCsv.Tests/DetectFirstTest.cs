using AutoYnabCsv.Contracts;
using AutoYnabCsv.Detectors;

namespace Tests;

[TestClass]
public sealed class DetectFirstTest
{
    [TestMethod]
    [DataRow("n26-download.csv", "n26")]
    [DataRow("dkb-giro.csv", "dkb.giro")]
    public void DetectKnownCsv(string inputFilename, string expectedSourceType)
    {
        var input = SampleTextOf(inputFilename);
        var detection = DetectFirst.Instance.TryDetect(input);
        Assert.IsTrue(detection.Successful);
        Assert.AreEqual(expectedSourceType, detection.SourceType);
    }
    
    [TestMethod]
    public void DetectUnknownCsv()
    {
        var input = SampleTextOf("unknown.csv");
        Assert.AreEqual(KnownSources.None, DetectFirst.Instance.TryDetect(input));
    }

    private const string SamplesDir = "../../../../data/samples/";

    private static string SampleTextOf(string sample)
    {
        var input = File.ReadAllText(Path.Join(SamplesDir, sample));
        return input;
    }
}

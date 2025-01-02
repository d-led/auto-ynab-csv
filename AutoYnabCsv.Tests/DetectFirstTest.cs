using AutoYnabCsv.Contracts;
using AutoYnabCsv.Detectors;

namespace Tests;

[TestClass]
public sealed class DetectFirstTest
{
    [TestMethod]
    [DataRow("n26-download.csv", KnownSources.N26Source)]
    [DataRow("dkb-giro.csv", KnownSources.DkbGiroSource)]
    [DataRow("dkb-visa.csv", KnownSources.DkbVisaSource)]
    public void DetectKnownCsv(string inputFilename, string expectedSourceType)
    {
        var input = TestHelpers.SampleTextOf(inputFilename);
        var detection = DetectFirst.Instance.TryDetect(input);
        Assert.IsTrue(detection.Successful);
        Assert.AreEqual(expectedSourceType, detection.SourceType);
    }

    [TestMethod]
    public void DetectUnknownCsv()
    {
        var input = TestHelpers.SampleTextOf("unknown.csv");
        Assert.AreEqual(KnownSources.None, DetectFirst.Instance.TryDetect(input));
    }
}

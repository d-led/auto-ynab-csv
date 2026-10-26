namespace AutoYnabCsv.Tests.Common;

using AutoYnabCsv.Common;

/// <summary>
/// A version number alone cannot answer "am I running the binary I just built?" — the build time can.
/// </summary>
[TestClass]
public sealed class BuildStampTest
{
    [TestMethod]
    public void TheLineSaysTheVersionAndWhenItWasBuilt()
    {
        var built = new DateTime(2026, 10, 22, 14, 31, 7, DateTimeKind.Utc);

        Assert.AreEqual("0.0.6 (built 2026-10-22 14:31 UTC)", BuildStamp.Line("0.0.6", built));
    }

    [TestMethod]
    public void TimeIsSaidInUtcSoTwoClocksAgree()
    {
        var local = new DateTime(2026, 10, 22, 16, 31, 0, DateTimeKind.Local);

        StringAssert.Contains(BuildStamp.Line("1.2.3", local), local.ToUniversalTime().ToString("HH:mm"));
        StringAssert.Contains(BuildStamp.Line("1.2.3", local), "UTC");
    }

    [TestMethod]
    public void WhenTheTimeIsUnknownTheLineStillSaysTheVersion()
    {
        Assert.AreEqual("dev (build time unknown)", BuildStamp.Line("dev", null));
    }

    [TestMethod]
    public void TheTimeIsTheOneTheFileWasWrittenAt()
    {
        var written = new DateTime(2026, 10, 22, 14, 31, 7, DateTimeKind.Utc);
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.stamp");

        try
        {
            File.WriteAllText(file, "built");
            File.SetLastWriteTimeUtc(file, written);

            Assert.AreEqual(written, BuildStamp.WrittenAt(file)!.Value.UtcDateTime);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void AFileThatIsNotThereSaysNothingRatherThanGuessing()
    {
        Assert.IsNull(BuildStamp.WrittenAt(null));
        Assert.IsNull(BuildStamp.WrittenAt(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gone")));
    }

    [TestMethod]
    public void ThisProcessKnowsWhichFileItRunsFrom()
    {
        Assert.IsTrue(File.Exists(BuildStamp.Location()), $"runs from a real file: {BuildStamp.Location()}");
    }
}

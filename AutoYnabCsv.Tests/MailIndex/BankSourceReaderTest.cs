using AutoYnabCsv.MailIndex.Web;

namespace Tests;

[TestClass]
public class BankSourceReaderTest
{
    [TestMethod]
    public void ARecognisedExportIsReadWithItsDetection()
    {
        var path = Write(
            """
            "Booking Date","Value Date","Partner Name","Partner Iban",Type,"Payment Reference","Account Name","Amount (EUR)","Original Amount","Original Currency","Exchange Rate"
            2024-12-12,2024-12-11,"Some bakery",,Presentment,,"Main Account",-5.95,5.95,EUR,1
            """
        );

        try
        {
            var (sources, skipped) = BankSourceReader.Read([path]);

            Assert.AreEqual(0, skipped.Count);
            Assert.AreEqual(1, sources.Count);
            Assert.AreEqual("n26", sources[0].Detected);
            Assert.AreEqual(1, sources[0].Entries.Count);
            Assert.AreEqual(5.95m, sources[0].Entries[0].Amount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnUnknownExportIsReportedAndNotRead()
    {
        var path = Write("a,b\n1,2");

        try
        {
            var (sources, skipped) = BankSourceReader.Read([path]);

            Assert.AreEqual(0, sources.Count);
            Assert.AreEqual(1, skipped.Count);
            Assert.AreEqual("format not supported", skipped[0].Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }
}

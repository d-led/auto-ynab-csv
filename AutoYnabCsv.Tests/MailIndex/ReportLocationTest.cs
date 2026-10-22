using AutoYnabCsv.MailIndex;

namespace Tests;

[TestClass]
public class ReportLocationTest
{
    [TestMethod]
    public void TheReportGoesBesideTheFirstExport()
    {
        WithTempFolder(first =>
            WithTempFolder(second =>
                Assert.AreEqual(
                    Path.Combine(first, ReportLocation.FolderName),
                    ReportLocation.For([first, second])
                )
            )
        );
    }

    [TestMethod]
    public void AFileDecidesByItsFolder()
    {
        WithTempFolder(folder =>
        {
            var file = Path.Combine(folder, "export.csv");
            File.WriteAllText(file, "Date,Payee,Memo,Inflow,Outflow");

            Assert.AreEqual(
                Path.Combine(folder, ReportLocation.FolderName),
                ReportLocation.For([file])
            );
        });
    }

    [TestMethod]
    public void WithoutInputTheDefaultFolderIsUsed()
    {
        Assert.AreEqual(AppDirectories.DefaultExportDirectory(), ReportLocation.For([]));
    }

    private static void WithTempFolder(Action<string> use)
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            use(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

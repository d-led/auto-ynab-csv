using AutoYnabCsv.MailIndex;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Index;

namespace Tests;

[TestClass]
public class ReportServiceTest
{
    [TestMethod]
    public void AConversionLeavesTheReportAndTheRowsToImportSideBySide()
    {
        WithTempFolder(folder =>
        {
            var exports = Path.Combine(folder, "exports");
            Directory.CreateDirectory(exports);
            File.WriteAllText(
                Path.Combine(exports, "dkb-giro.csv"),
                TestHelpers.SampleTextOf("dkb-giro.csv")
            );

            var indexPath = Path.Combine(folder, "mail-index.sqlite");
            using var index = MailIndexStore.Open(indexPath);
            var context = ReportContext.For(
                index,
                indexPath,
                new ExtractionRuleSet([]),
                "test",
                mailboxRoot: Path.Combine(folder, "no-mail")
            );

            var run = ReportService.Write(
                context,
                [exports],
                ReportLocation.For([exports])
            );

            Assert.IsNull(run.Sync);

            Assert.IsTrue(File.Exists(run.Export.HtmlPath));
            Assert.IsTrue(File.Exists(run.Export.AuditPath));
            Assert.AreEqual(1, run.Export.CsvPaths.Count);
            StringAssert.StartsWith(
                File.ReadAllText(run.Export.CsvPaths[0]),
                "Date,Payee,Memo,Inflow,Outflow"
            );
            Assert.AreEqual(1, run.Audit.Inputs.Count);
        });
    }

    [TestMethod]
    public void AnUnreadableMailboxIsReportedRatherThanStoppingTheReport()
    {
        WithTempFolder(folder =>
        {
            using var index = MailIndexStore.Open(Path.Combine(folder, "mail-index.sqlite"));

            var context = ReportContext.For(
                index,
                Path.Combine(folder, "mail-index.sqlite"),
                new ExtractionRuleSet([]),
                "test",
                mailboxRoot: Path.Combine(folder, "no-mail-here")
            );

            Assert.AreEqual("unavailable", context.MailboxRoot);
            Assert.IsNull(context.Finder);
        });
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

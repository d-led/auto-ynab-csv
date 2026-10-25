using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class BankStatementReaderTest
{
    [TestMethod]
    public void AnExportIsReadIntoRowsAndABalance()
    {
        var statement = BankStatementReader.TryRead(TestHelpers.SamplePathOf("dkb-giro.csv"));

        Assert.IsNotNull(statement);
        Assert.AreEqual("dkb-giro", statement.Label, "a sample has no date prefix and no IBAN to shorten");
        Assert.AreEqual(1234.50m, statement.ClosingBalance);
        Assert.AreEqual(5, statement.Rows.Count);
        Assert.AreEqual(-845.41m, Money.Sum(statement.Rows.Select(row => row.Amount)), "outflows are negative amounts, inflows positive");
    }

    [TestMethod]
    public void TheRowsAreInDateOrderSoAReportReadsLikeAStatement()
    {
        var statement = BankStatementReader.TryRead(TestHelpers.SamplePathOf("dkb-giro.csv"))!;

        CollectionAssert.AreEqual(
            statement.Rows.OrderBy(row => row.Date).Select(row => row.Date).ToArray(),
            statement.Rows.Select(row => row.Date).ToArray()
        );
    }

    [TestMethod]
    public void AFileThisToolWroteIsNotMistakenForAnExport()
    {
        var folder = Directory.CreateTempSubdirectory("auto-ynab-csv-ynab-test").FullName;
        try
        {
            File.Copy(TestHelpers.SamplePathOf("dkb-giro.csv"), Path.Combine(folder, "export.ynab-20260101000000.csv"));

            Assert.AreEqual(0, BankStatementReader.ReadFolder(folder).Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

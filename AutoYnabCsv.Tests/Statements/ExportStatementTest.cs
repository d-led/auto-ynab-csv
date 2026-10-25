using AutoYnabCsv.Contracts;
using AutoYnabCsv.Statements;

namespace Tests.Statements;

[TestClass]
public class ExportStatementTest
{
    [TestMethod]
    public void ADkbGiroExportNamesItsAccountAndTheBalanceOfItsLastDay()
    {
        var statement = StatementOf("dkb-giro.csv", KnownSources.DkbGiroSource);

        Assert.AreEqual("Girokonto", statement.AccountName);
        Assert.AreEqual("DE00000000000000000002", statement.AccountIdentifier);
        Assert.AreEqual(new DateOnly(2024, 12, 1), statement.PeriodFrom);
        Assert.AreEqual(new DateOnly(2024, 12, 31), statement.PeriodTo);
        Assert.AreEqual(new DateOnly(2024, 12, 31), statement.BalanceDate);
        Assert.AreEqual(1234.50m, statement.ClosingBalance);
    }

    [TestMethod]
    public void ACreditCardSaldoWrittenWithADoubledMinusIsOwedNotOwing()
    {
        var statement = StatementOf("dkb-visa.csv", KnownSources.DkbVisaSource);

        Assert.AreEqual("Visa Kreditkarte", statement.AccountName);
        Assert.AreEqual(new DateOnly(2025, 1, 2), statement.BalanceDate);
        Assert.AreEqual(-123.45m, statement.ClosingBalance);
    }

    [TestMethod]
    public void AnAccountWithoutABalanceLineLeavesTheBalanceUnknown()
    {
        var statement = StatementOf("n26-download.csv", KnownSources.N26Source);

        Assert.IsNull(statement.ClosingBalance);
        Assert.IsNull(statement.BalanceDate);
    }

    private static ExportStatement StatementOf(string sample, string sourceType) =>
        ExportStatementReader.Read(TestHelpers.SampleTextOf(sample), sourceType);
}

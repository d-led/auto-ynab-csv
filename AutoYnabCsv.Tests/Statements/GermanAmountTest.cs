using System.Globalization;
using AutoYnabCsv.Statements;

namespace Tests.Statements;

[TestClass]
public class GermanAmountTest
{
    [TestMethod]
    [DataRow("1.234,50 €", "1234.50")]
    [DataRow("-3.358,67 €", "-3358.67")]
    [DataRow("306,62 €", "306.62")]
    [DataRow("--87,58 EUR", "-87.58")]
    [DataRow("--353,66999999999996 EUR", "-353.67")]
    [DataRow("100", "100")]
    [DataRow("-2,49", "-2.49")]
    public void MoneyAsTheBankWroteIt(string written, string expected)
    {
        Assert.IsTrue(GermanAmount.TryParse(written, out var amount), $"'{written}' should parse");
        Assert.AreEqual(Amount(expected), amount);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("EUR")]
    [DataRow("--")]
    public void NothingThatIsNotAMoneyAmount(string written)
    {
        Assert.IsFalse(GermanAmount.TryParse(written, out _));
    }

    private static decimal Amount(string invariant) =>
        decimal.Parse(invariant, NumberStyles.Number, CultureInfo.InvariantCulture);
}

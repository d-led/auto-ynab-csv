using AutoYnabCsv.MailIndex.Facts;

namespace Tests;

[TestClass]
public class AmountParserTest
{
    [TestMethod]
    public void ParsesGermanGrouping()
    {
        Assert.IsTrue(AmountParser.TryParse("1.234,56", out var amount));
        Assert.AreEqual(1234.56m, amount);
    }

    [TestMethod]
    public void ParsesEnglishGrouping()
    {
        Assert.IsTrue(AmountParser.TryParse("1,234.56", out var amount));
        Assert.AreEqual(1234.56m, amount);
    }

    [TestMethod]
    public void ParsesASingleCommaAsTheDecimalSeparator()
    {
        Assert.IsTrue(AmountParser.TryParse("42,99", out var amount));
        Assert.AreEqual(42.99m, amount);
    }

    [TestMethod]
    public void RefusesText()
    {
        Assert.IsFalse(AmountParser.TryParse("EUR", out _));
    }
}

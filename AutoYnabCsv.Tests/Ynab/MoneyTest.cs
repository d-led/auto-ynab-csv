using AutoYnabCsv.Ynab.Contracts;

namespace Tests.Ynab;

[TestClass]
public class MoneyTest
{
    [TestMethod]
    public void MilliunitsAreThousandthsOfTheCurrency()
    {
        Assert.AreEqual(4924.34m, new Milliunits(4_924_340).ToMoney());
        Assert.AreEqual(-2.99m, new Milliunits(-2_990).ToMoney());
    }

    [TestMethod]
    public void AnAmountSurvivesTheRoundTripThroughMilliunits()
    {
        Assert.AreEqual(-45.81m, ((Money)(-45.81m)).ToMilliunits().ToMoney());
        Assert.AreEqual(-45810L, ((Money)(-45.81m)).ToMilliunits().Value);
    }

    [TestMethod]
    public void AddingAmountsKeepsTheCents()
    {
        Assert.AreEqual(-845.41m, Money.Sum([-2.75m, -75m, -1001m, -1001.22m, 1234.56m]));
    }

    [TestMethod]
    public void AnAmountIsPrintedWithTwoPlacesAndAPoint()
    {
        Assert.AreEqual("-3358.67", ((Money)(-3358.67m)).ToString());
        Assert.AreEqual("0.00", Money.Zero.ToString());
    }

    [TestMethod]
    public void MilliunitsDoNotPassForMoneyWhenPrinted()
    {
        StringAssert.Contains(new Milliunits(4_924_340).ToString(), "Milliunits", "printing one by mistake is visible, not silent");
    }
}

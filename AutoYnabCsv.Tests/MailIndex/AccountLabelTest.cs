using AutoYnabCsv.MailIndex.Web;

namespace Tests;

[TestClass]
public class AccountLabelTest
{
    [TestMethod]
    public void AnIbanInTheNameBecomesTheLastFourDigits()
    {
        Assert.AreEqual(
            "Girokonto · 0001",
            AccountLabel.Of("19-10-2026_Umsatzliste_Girokonto_DE00000000000000000001.csv")
        );
    }

    [TestMethod]
    public void ASuffixAfterTheIbanDoesNotConfuseTheLabel()
    {
        Assert.AreEqual(
            "Girokonto u18 · 0002",
            AccountLabel.Of(
                "19-10-2026_Umsatzliste_Girokonto u18_DE00000000000000000002-xiu-giro.csv"
            )
        );
    }

    [TestMethod]
    public void ATrailingCardNumberIsKept()
    {
        Assert.AreEqual(
            "Visa Kreditkarte · 0000",
            AccountLabel.Of("19-10-2026_Umsatzliste_Visa Kreditkarte_0000.csv")
        );
    }

    [TestMethod]
    public void AUuidNameFallsBackToItsFirstWord()
    {
        Assert.AreEqual(
            "n26",
            AccountLabel.Of("n26-00000000-0000-0000-0000-000000000000.csv")
        );
    }
}

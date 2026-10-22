using AutoYnabCsv.MailIndex.Enrichment;

namespace Tests;

[TestClass]
public class EntryPatternsTest
{
    [TestMethod]
    public void PaypalWritesTheMerchantIntoTheMemo()
    {
        var facts = EntryPatterns.Of(
            "PayPal Europe S.a.r.l. et Cie S.C.A                                   22-24 Boulevard Royal, 2449 Luxembourg",
            "1050000000001/. Thomann GmbH, Ihr Einkauf bei Thomann GmbH"
        );

        Assert.AreEqual("Thomann GmbH", facts.Merchant);
        StringAssert.Contains(facts.Payee, "PayPal Europe");
        Assert.IsFalse(facts.Payee.Contains("  ", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AmazonOrderNumberIsReadFromTheMemo()
    {
        var facts = EntryPatterns.Of(
            "AMAZON PAYMENTS EUROPE S.C.A.",
            "000-0000000-0000002 Amazon.de 37NZA79HX021Q92T"
        );

        Assert.AreEqual("Amazon", facts.Merchant);
        Assert.AreEqual("000-0000000-0000002", facts.OrderNumber);
    }

    [TestMethod]
    public void AnN26PaypalStarKeepsTheTruncatedMerchant()
    {
        var facts = EntryPatterns.Of("PAYPAL *AUDIOPARTNE", "Presentment");

        Assert.AreEqual("PayPal *AUDIOPARTNE", facts.Merchant);
    }

    [TestMethod]
    public void APlainPayeeStaysItself()
    {
        var facts = EntryPatterns.Of("Edeka Ernst Ostbahnhof", string.Empty);

        Assert.AreEqual("Edeka Ernst Ostbahnhof", facts.Merchant);
        Assert.AreEqual(string.Empty, facts.OrderNumber);
    }

    [TestMethod]
    public void GenericTransferWordsAreRecognised()
    {
        var facts = EntryPatterns.Of("Some Bank", "Umbuchung auf Tagesgeld");

        Assert.IsTrue(facts.LooksLikeTransfer);
    }
}

using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class SignalExtractorTest
{
    private static readonly DateTimeOffset Received = new(2024, 12, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AnAmazonConfirmationYieldsVendorOrderAndAmount()
    {
        var facts = SignalExtractor.Extract(
            Mail(
                "Ihre Bestellung 302-1234567-8901234",
                "ship-confirm@amazon.de",
                "Gesamtsumme: EUR 42,99"
            )
        );

        Assert.AreEqual("Amazon", facts.Vendor);
        CollectionAssert.AreEqual(new[] { "302-1234567-8901234" }, facts.OrderNumbers.ToArray());
        CollectionAssert.AreEqual(new[] { 42.99m }, facts.Amounts.ToArray());
    }

    [TestMethod]
    public void AMailWithoutSignalsYieldsNone()
    {
        var facts = SignalExtractor.Extract(
            Mail("Newsletter", "news@example.com", "Nothing to see here.")
        );

        Assert.AreEqual("example.com", facts.Vendor);
        Assert.AreEqual(0, facts.OrderNumbers.Count);
        Assert.AreEqual(0, facts.Amounts.Count);
    }

    [TestMethod]
    public void ADateIsNotMistakenForAnAmount()
    {
        var facts = SignalExtractor.Extract(
            Mail("Termin", "info@example.com", "Am 12.12.2024 um 10 Uhr.")
        );

        Assert.AreEqual(0, facts.Amounts.Count);
    }

    [TestMethod]
    public void ItemsUnfoldFromWhatWasBought()
    {
        var facts = SignalExtractor.Extract(
            Mail("Bestellung", "order@amazon.de", "1 x Pickboy 0,88 mm\n3 x Saiten 12,50 €"),
            RuleSetLoader.Shipped()
        );

        CollectionAssert.AreEqual(
            new[] { "Pickboy 0,88 mm", "Saiten 12,50 €" },
            facts.Items.ToArray()
        );
    }

    private static MailRecord Mail(string subject, string from, string body) =>
        new("message.emlx", Received, subject, from, string.Empty, body);
}

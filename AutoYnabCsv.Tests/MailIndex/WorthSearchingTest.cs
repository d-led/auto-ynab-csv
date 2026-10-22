using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Extraction;

namespace Tests;

[TestClass]
public class WorthSearchingTest
{
    private static readonly DateOnly Day = new(2026, 10, 2);
    private static readonly ExtractionRuleSet Rules = RuleSetLoader.Shipped();

    [TestMethod]
    public void AProcessorPayeeIsWorthASearch()
    {
        CollectionAssert.AreEqual(
            new[] { 0 },
            Chosen(Entry("PayPal Europe S.a.r.l. et Cie S.C.A"))
        );
    }

    [TestMethod]
    public void APayeeTheBankMaskedIsWorthASearch()
    {
        CollectionAssert.AreEqual(new[] { 0 }, Chosen(Entry("....................")));
    }

    [TestMethod]
    public void AMerchantTheRowNamesIsNotWorthASearch()
    {
        CollectionAssert.AreEqual(new int[0], Chosen(Entry("EDEKA"), Entry("Wohnbau GmbH")));
    }

    [TestMethod]
    public void MoneyThatMerelyMovedIsNotWorthASearch()
    {
        CollectionAssert.AreEqual(
            new int[0],
            Chosen(
                Entry("Deutsche Kreditbank Berlin", 1, "KREDITKARTENABRECHNUNG VISA -ABR. 4748"),
                Entry("DEUTSCHE KREDITBANK AG", 2, "Bareinzahlung Cash im Shop")
            )
        );
    }

    [TestMethod]
    public void MoneyAPersonSentIsWorthASearch()
    {
        // "PAYPAL, INSTANT TRANSFER, 85" is only an amount; the mail that goes with it names
        // Ralf Beispiel and the code of the payment, which is what the row is missing.
        CollectionAssert.AreEqual(new[] { 0 }, Chosen(Entry("PAYPAL", 0, "INSTANT TRANSFER")));
    }

    private static int[] Chosen(params CorrelationEntry[] entries) =>
        WorthSearching.Of(entries, Rules).Select(entry => entry.Index).ToArray();

    private static CorrelationEntry Entry(string payee, int index = 0, string memo = "VISA Debitkartenumsatz") =>
        new(index, Day, payee, 12.34m, memo);
}

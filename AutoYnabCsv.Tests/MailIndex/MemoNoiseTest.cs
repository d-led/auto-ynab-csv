using AutoYnabCsv.MailIndex.Enrichment;

namespace Tests.MailIndex;

[TestClass]
public class MemoNoiseTest
{
    [TestMethod]
    public void ALineThatSaysWhatElseToOneBuyLeavesNothingBehind()
    {
        var cleaned = MemoNoise.Without("Beispiel Kopfhörer · Return or replace items in Your Orders");

        Assert.AreEqual("Beispiel Kopfhörer ·", cleaned.TrimEnd());
    }

    [TestMethod]
    public void ThePhraseIsRecognisedHoweverTheShopCasesIt()
    {
        Assert.AreEqual(
            "Beispiel Kopfhörer",
            MemoNoise.Without("Beispiel Kopfhörer VIEW OR EDIT ORDER").Trim()
        );
    }

    [TestMethod]
    public void ATitleThatMerelyContainsOneOfTheWordsSurvives()
    {
        const string bought = "Beispiel Ersatzteil für Ihre Bestellung";

        Assert.AreEqual(bought, MemoNoise.Without(bought));
    }

    [TestMethod]
    public void AMemoOfNothingButNoiseBecomesEmpty()
    {
        Assert.AreEqual(string.Empty, MemoNoise.Without("Privacy Notice").Trim());
    }

    [TestMethod]
    public void AnAddressIsSomebodysDataAndNeverBelongsToAMemo()
    {
        var cleaned = MemoNoise.Without("E-mail Address: someone@example.com · Kopfhörer");

        Assert.IsFalse(cleaned.Contains('@'), $"no address is left: {cleaned}");
        StringAssert.Contains(cleaned, "Kopfhörer", "and the purchase stays");
    }

    [TestMethod]
    public void TheSentenceAfterTheOrderNumberGoesAsASentenceNotItsTail()
    {
        var cleaned = MemoNoise.Without(
            "Kopfhörer · You can view the status of your order or make changes to it by visiting Your Orders · geliefert"
        );

        Assert.IsFalse(cleaned.Contains("view the status", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cleaned.Contains("visiting", StringComparison.OrdinalIgnoreCase), $"tail left: {cleaned}");
        StringAssert.Contains(cleaned, "Kopfhörer");
    }

    [TestMethod]
    public void TheProductCardsOwnLabelsGo()
    {
        Assert.AreEqual("Kopfhörer", MemoNoise.Without("Condition: New Kopfhörer").Trim());
        Assert.AreEqual("Kopfhörer", MemoNoise.Without("Kopfhörer Shipment 2 of 2").Trim());
    }

    [TestMethod]
    public void AnOrderNumberIsEvidenceAndStays()
    {
        const string reference = "Order 028-3613296-4739534";

        Assert.AreEqual(reference, MemoNoise.Without(reference));
    }

    [TestMethod]
    public void TwoWordsEitherSideOfARemovedPhraseAreNotFused()
    {
        var cleaned = MemoNoise.Without("Kopfhörer Transaktionsdetails geliefert");

        CollectionAssert.AreEqual(
            new[] { "Kopfhörer", "geliefert" },
            cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            "a space is left where the phrase was, so the words stay two words"
        );
    }
}

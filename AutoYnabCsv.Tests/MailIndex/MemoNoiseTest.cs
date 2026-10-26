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

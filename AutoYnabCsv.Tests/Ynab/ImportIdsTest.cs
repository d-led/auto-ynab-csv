using AutoYnabCsv.Ynab.Apply;

namespace Tests.Ynab;

[TestClass]
public class ImportIdsTest
{
    [TestMethod]
    public void TheSameMovementAlwaysGetsTheSameImportId()
    {
        var first = ImportIds.Of("acc", new DateOnly(2026, 10, 23), -45.81m, "Kaufland", "VISA Debitkartenumsatz");
        var again = ImportIds.Of("acc", new DateOnly(2026, 10, 23), -45.81m, "Kaufland", "VISA Debitkartenumsatz");

        Assert.AreEqual(first, again, "so applying the same plan twice is safe");
    }

    [TestMethod]
    public void PunctuationAndCaseAreNoiseLikeTheyAreEverywhereElse()
    {
        var plain = ImportIds.Of("acc", new DateOnly(2026, 10, 23), -45.81m, "Kaufland", "Kartenzahlung");
        var shouted = ImportIds.Of("acc", new DateOnly(2026, 10, 23), -45.81m, "KAUFLAND!", "kartenzahlung");

        Assert.AreEqual(plain, shouted);
    }

    [TestMethod]
    public void ADifferentMovementGetsADifferentImportId()
    {
        var first = ImportIds.Of("acc", new DateOnly(2026, 10, 23), -45.81m, "Kaufland", "VISA");
        var other = ImportIds.Of("acc", new DateOnly(2026, 10, 24), -45.81m, "Kaufland", "VISA");

        Assert.AreNotEqual(first, other);
    }

    [TestMethod]
    public void TheImportIdFitsWhatYnabAccepts()
    {
        var id = ImportIds.Of("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", new DateOnly(2026, 10, 23), -45.81m, "Kaufland", "VISA");

        Assert.IsTrue(id.Length <= 36, "YNAB refuses an import id longer than 36 characters");
    }
}

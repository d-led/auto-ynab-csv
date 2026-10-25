using System.Text;
using AutoYnabCsv.Contracts;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Interactions;

namespace Tests;

[TestClass]
public class TransferNamingTest
{
    private const string OwnNumber = ExportFixture.OwnNumber;

    [TestMethod]
    public void AMovementToMyOwnAccountIsNamedAsTheTransferItIs()
    {
        var entries = Convert(Book("Tagesgeld · 3267"));

        Assert.AreEqual("Transfer : Tagesgeld · 3267", entries[0].Payee);
        Assert.AreEqual("Sparrate · …0003", entries[0].Memo, "the number the export printed stays readable");
    }

    [TestMethod]
    public void APaymentToSomebodyElseKeepsThePayeeTheExportGaveIt()
    {
        var entries = Convert(Book("Tagesgeld · 3267"));

        Assert.AreEqual("Some Shop", entries[1].Payee);
        Assert.AreEqual("Einkauf", entries[1].Memo);
    }

    [TestMethod]
    public void WithoutKnowingMyAccountsNothingIsCalledATransfer()
    {
        var entries = Convert(OwnAccountBook.None);

        Assert.AreEqual("Me", entries[0].Payee);
    }

    [TestMethod]
    public void AnAccountNumberWrittenInGroupsIsTheSameAccount()
    {
        var book = new OwnAccountBook([("DE00 0000 0000 0000 0000 03", "Tagesgeld · 3267")]);

        Assert.AreEqual("Tagesgeld · 3267", book.NameOf(OwnNumber));
        Assert.IsNull(book.NameOf("DE99999999999999999999"), "somebody else's number is not an own account");
        Assert.IsNull(book.NameOf("0003"), "four digits alone are too little to be sure");
    }

    [TestMethod]
    public void AnEmptyMemoBecomesJustTheHint()
    {
        Assert.AreEqual("…0003", TransferNaming.Memo(string.Empty, OwnNumber, Book("Tagesgeld · 3267")));
    }

    [TestMethod]
    public void TheFolderKnowsWhichOfTheExportsAreMine()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ayc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        var mine = Path.Combine(folder, "19-10-2026_Umsatzliste_Girokonto_DE00000000000000000002.csv");
        var other = Path.Combine(folder, "19-10-2026_Umsatzliste_Tagesgeld_DE00000000000000000003.csv");
        File.WriteAllText(mine, ExportFixture.DkbGiro(), Encoding.UTF8);
        File.WriteAllText(other, ExportFixture.DkbGiro(OwnNumber), Encoding.UTF8);

        var converted = TextConversion.ConvertFile(mine);

        StringAssert.Contains(converted, "Transfer : Tagesgeld · 0003", "the other export in the folder is my own account");
    }

    private static IReadOnlyList<YnabImportEntry> Convert(OwnAccountBook own) =>
        [.. new DkbGiroConverter(own).Convert(ExportFixture.DkbGiro())];

    private static OwnAccountBook Book(string name) => new([(OwnNumber, name)]);
}

using System.Text;
using AutoYnabCsv.Contracts;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Interactions;

namespace Tests;

[TestClass]
public class TransferNamingTest
{
    private const string OwnNumber = "DE00000000000000000003";

    private const string DkbGiro = """
        "Girokonto";"DE00000000000000000002"
        "Zeitraum:";"01.12.2024 - 31.12.2024"
        "Kontostand vom 31.12.2024:";"1.234,50 €"
        ""
        "Buchungsdatum";"Wertstellung";"Status";"Zahlungspflichtige*r";"Zahlungsempfänger*in";"Verwendungszweck";"Umsatztyp";"IBAN";"Betrag (€)";"Gläubiger-ID";"Mandatsreferenz";"Kundenreferenz"
        "13.12.24";"12.12.24";"Gebucht";"Me";"Me";"Sparrate";"Ausgang";"DE00000000000000000003";"-500";"","",""
        "14.12.24";"13.12.24";"Gebucht";"Some Shop";"Some Shop";"Einkauf";"Ausgang";"DE99999999999999999999";"-12,50";"","",""
        """;

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
        File.WriteAllText(mine, DkbGiro, Encoding.UTF8);
        File.WriteAllText(other, WithAccount("DE00000000000000000003"), Encoding.UTF8);

        var converted = TextConversion.ConvertFile(mine);

        StringAssert.Contains(converted, "Transfer : Tagesgeld · 0003", "the other export in the folder is my own account");
    }

    private static IReadOnlyList<YnabImportEntry> Convert(OwnAccountBook own) =>
        [.. new DkbGiroConverter(own).Convert(DkbGiro)];

    private static OwnAccountBook Book(string name) => new([(OwnNumber, name)]);

    private static string WithAccount(string iban) => DkbGiro.Replace("DE00000000000000000002", iban, StringComparison.Ordinal);
}

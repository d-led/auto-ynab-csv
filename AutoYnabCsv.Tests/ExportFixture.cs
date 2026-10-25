using System.Text;

namespace Tests;

/// <summary>A DK B giro export: one row to an account of your own, one to somebody else's.</summary>
internal static class ExportFixture
{
    public const string OwnNumber = "DE00000000000000000003";

    public const string AnotherNumber = "DE00000000000000000002";

    private const string Dkb = """
        "Girokonto";"DE00000000000000000002"
        "Zeitraum:";"01.12.2024 - 31.12.2024"
        "Kontostand vom 31.12.2024:";"1.234,50 €"
        ""
        "Buchungsdatum";"Wertstellung";"Status";"Zahlungspflichtige*r";"Zahlungsempfänger*in";"Verwendungszweck";"Umsatztyp";"IBAN";"Betrag (€)";"Gläubiger-ID";"Mandatsreferenz";"Kundenreferenz"
        "13.12.24";"12.12.24";"Gebucht";"Me";"Me";"Sparrate";"Ausgang";"DE00000000000000000003";"-500";"","",""
        "14.12.24";"13.12.24";"Gebucht";"Some Shop";"Some Shop";"Einkauf";"Ausgang";"DE99999999999999999999";"-12,50";"","",""
        """;

    /// <summary>The same export, printed for the account whose number is given.</summary>
    public static string DkbGiro(string accountIban = AnotherNumber) =>
        Dkb.Replace(AnotherNumber, accountIban, StringComparison.Ordinal);

    public static string Write(string folder, string fileName, string text)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, text, Encoding.UTF8);
        return path;
    }
}

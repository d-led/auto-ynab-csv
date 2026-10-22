namespace AutoYnabCsv.MailIndex.Correlation;

/// <summary>
/// A shop's own name, without the legal form that only a contract needs. A receipt that says
/// <c>eBay S.a.r.l.</c> and a mailbox that signs <c>eBay</c> name the same shop, and a search for
/// that shop's mail has to ask in the words the mail itself uses.
/// </summary>
public static class ShopName
{
    /// <summary><c>eBay S.a.r.l.</c> is eBay, <c>Thomann GmbH</c> is Thomann.</summary>
    public static string Head(string shop)
    {
        var words = shop.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = words.Length;

        while (kept > 1 && LegalForms.Contains(Bare(words[kept - 1])))
        {
            kept--;
        }

        return string.Join(' ', words[..kept]).TrimEnd(',', '.');
    }

    private static string Bare(string word) => word.Replace(".", "").Replace(",", "");

    private static readonly HashSet<string> LegalForms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "sarl",
            "sca",
            "gmbh",
            "ag",
            "se",
            "kg",
            "ohg",
            "gbr",
            "ek",
            "ltd",
            "limited",
            "inc",
            "llc",
            "co",
            "cie",
            "et",
        };
}

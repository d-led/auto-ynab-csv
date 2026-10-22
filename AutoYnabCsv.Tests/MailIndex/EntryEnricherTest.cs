using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Enrichment;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class EntryEnricherTest
{
    private static readonly DateOnly Purchased = new(2026, 10, 2);
    private static readonly DateTimeOffset Received = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void TheMemoCarriesTheItemsAndTheOrderNumberFromTheMail()
    {
        var mail = Mail(
            vendor: "Thomann",
            orderNumbers: ["302-1234567-8901234"],
            fields: new Dictionary<string, string>
            {
                ["items"] = "Kabel 3,50 €\nSaitensatz 12,50 €\nStimmgerät 9,99 €",
            }
        );

        var enriched = EntryEnricher.Of(Entry("PAYPAL *THOMANN", 177.29m), mail);

        Assert.AreEqual(
            "Order 302-1234567-8901234 · Kabel 3,50 € · Saitensatz 12,50 € · Stimmgerät 9,99 €",
            enriched.Memo
        );
    }

    [TestMethod]
    public void ANumberTheBankShowsIsNotAPersonAndTheCodeIsWhatFollows()
    {
        // The bank row names PayPal and its own reference; the mail names the person the money
        // went to, and the code that answers a question at PayPal. Nothing else is worth saying.
        var mail = Mail(
            vendor: "PayPal",
            fields: new Dictionary<string, string>
            {
                ["recipient"] = "Jan Beispiel",
                ["transactionId"] = "0W702662EH627104L",
            }
        );

        var enriched = EntryEnricher.Of(
            Entry(
                "PayPal Europe S.a.r.l. et Cie S.C.A",
                65.00m,
                "1000000000002/PP.8625.PP/. , Ihr Einkauf bei"
            ),
            mail
        );

        Assert.AreEqual("Jan Beispiel", enriched.Payee);
        Assert.AreEqual("Transaktion 0W702662EH627104L · via paypal", enriched.Memo);
    }

    [TestMethod]
    public void MoneyThatOnlyWentToPayPalKeepsItsCodeAndNothingIsInvented()
    {
        // Both halves of a transfer between the bank account and PayPal: nothing was bought and
        // nobody else was paid, so the code is the whole story the mail has to tell.
        var mail = Mail(
            vendor: "PayPal",
            fields: new Dictionary<string, string>
            {
                ["transactionId"] = "8MV091879G771883W",
            }
        );

        var enriched = EntryEnricher.Of(
            Entry("PayPal (Europe) Sarl", 58.56m, "beispiel example.com"),
            mail
        );

        Assert.AreEqual("PayPal (Europe) Sarl", enriched.Payee);
        Assert.AreEqual("Transaktion 8MV091879G771883W", First(enriched), "the code leads");
        Assert.IsFalse(enriched.Memo.Contains("via", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TheCodeIsNotRepeatedEvenWhenTheBankPrintsIt()
    {
        // The bank sometimes quotes the same code. It leads the memo once, not twice.
        var mail = Mail(
            vendor: "PayPal",
            fields: new Dictionary<string, string>
            {
                ["transactionId"] = "8MV091879G771883W",
            }
        );

        var enriched = EntryEnricher.Of(
            Entry("PayPal Europe S.a.r.l. et Cie S.C.A", 58.56m, "8MV091879G771883W PAYPAL"),
            mail
        );

        Assert.AreEqual("Transaktion 8MV091879G771883W", enriched.Memo);
    }

    [TestMethod]
    public void ALinkIsNotSomethingYouBought()
    {
        // The footer of a shop's mail is full of URLs; a memo of links explains nothing.
        var mail = Mail(
            vendor: "Amazon",
            fields: new Dictionary<string, string>
            {
                ["items"] =
                    "Beispiel Kabelbinder 60 Stück\n"
                    + "https://www.amazon.de/gp/css/order-history?ref_=fed_yo_default\n"
                    + "https://www.amazon.de/your-account?ref_=fed_ya_default",
            }
        );

        var enriched = EntryEnricher.Of(Entry("Amazon", 20.58m), mail);

        Assert.AreEqual("Beispiel Kabelbinder 60 Stück", First(enriched));
        Assert.IsFalse(enriched.Memo.Contains("http", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ATitleIsKeptWhole()
    {
        var title =
            "Beispiel Spannfedern 10 Stück, 12 mm Durchmesser, "
            + "freie Länge 10 mm, Edelstahl, mit Doppelhaken";
        var mail = Mail(
            vendor: "Amazon",
            fields: new Dictionary<string, string> { ["items"] = title }
        );

        var enriched = EntryEnricher.Of(Entry("Amazon", 20.58m), mail);

        Assert.AreEqual(title, First(enriched));
    }

    [TestMethod]
    public void EveryItemOfAParcelIsListedInFullBehindItsOrderNumber()
    {
        var springs = "Beispiel Federn-Set 12 Größen Edelstahl 12 Sizes";
        var tension =
            "Beispiel Spannfedern 10 Stück, 12 mm, Edelstahl";
        var mail = Mail(
            vendor: "Amazon",
            orderNumbers: ["000-0000000-0000001"],
            fields: new Dictionary<string, string> { ["items"] = springs + "\n" + tension }
        );

        var enriched = EntryEnricher.Of(Entry("Amazon", 20.58m, "AMZN Mktp DE"), mail);

        Assert.AreEqual(
            $"Order 000-0000000-0000001 · {springs} · {tension} · AMZN Mktp DE",
            enriched.Memo
        );
    }

    [TestMethod]
    public void TheNumberLeadsAndIsNotRepeatedInTheBanksOwnWords()
    {
        // The bank writes "028-… AMZN Mktp DE 1ZSGK…" and the mail knows the number; printing the
        // number first and the bank's remainder after it says each thing once.
        var mail = Mail(vendor: "Amazon", orderNumbers: ["000-0000000-0000001"]);

        var enriched = EntryEnricher.Of(
            Entry("Amazon", 20.58m, "000-0000000-0000001 AMZN Mktp DE 1ZSGK1383666KZ20"),
            mail
        );

        Assert.AreEqual("Order 000-0000000-0000001 · AMZN Mktp DE", enriched.Memo);
    }

    [TestMethod]
    public void TheMarketplaceCodeAloneIsNotWorthKeeping()
    {
        var enriched = EntryEnricher.Of(
            Entry("Amazon", 20.58m, "AMZN Mktp DE 1ZSGK1383666KZ20"),
            (MailFacts?)null
        );

        Assert.AreEqual("AMZN Mktp DE", enriched.Memo);
    }

    [TestMethod]
    public void TheAmountStaysInItsOwnColumn()
    {
        var enriched = EntryEnricher.Of(
            Entry(
                "SWM Versorgungs GmbH",
                84m,
                "Musterstr. 1 .2.R, Abschlag Strom 09/2026 Betrag 84,00 Eur"
            ),
            (MailFacts?)null
        );

        Assert.AreEqual("Musterstr. 1 .2.R, Abschlag Strom 09/2026", enriched.Memo);
    }

    [TestMethod]
    public void APriceThatIsNotTheAmountIsKept()
    {
        var mail = Mail(
            vendor: "Thomann",
            fields: new Dictionary<string, string> { ["items"] = "Kabel 3,50 €" }
        );

        var enriched = EntryEnricher.Of(Entry("PAYPAL *THOMANN", 177.29m), mail);

        Assert.AreEqual("Kabel 3,50 €", First(enriched));
    }

    [TestMethod]
    public void AnOrderNumberOnlyTheMailKnowsIsAdded()
    {
        var mail = Mail(vendor: "Amazon", orderNumbers: ["000-0000000-0000001"]);

        var enriched = EntryEnricher.Of(Entry("Amazon", 20.58m, "AMZN Mktp DE"), mail);

        Assert.AreEqual("Order 000-0000000-0000001 · AMZN Mktp DE", enriched.Memo);
    }

    [TestMethod]
    public void TheServiceStaysQuietWhenTheRowNamesIt()
    {
        var enriched = EntryEnricher.Of(Entry("PayPal Europe S.a.r.l.", 42.00m), Mail(vendor: "PayPal"));

        Assert.IsFalse(enriched.Memo.Contains("via paypal", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TheMerchantInTheMemosWinsOverTheAggregatorPayee()
    {
        var entry = new CorrelationEntry(
            0,
            Purchased,
            "PayPal Europe S.a.r.l.",
            177.29m,
            "1050000000001/. Thomann GmbH, Ihr Einkauf bei Thomann GmbH"
        );

        var enriched = EntryEnricher.Of(entry, Mail(vendor: "PayPal"));

        Assert.AreEqual("Thomann GmbH", enriched.Payee);
    }

    [TestMethod]
    public void WithoutAMailTheRowStaysAsTheBankWroteIt()
    {
        var entry = Entry("Some Shop", 12.00m, memo: "Card payment");

        var enriched = EntryEnricher.Of(entry, mail: null);

        Assert.AreEqual("Some Shop", enriched.Payee);
        Assert.AreEqual("Card payment", enriched.Memo);
    }

    [TestMethod]
    public void TheBankMemoIsKeptBehindTheGist()
    {
        var enriched = EntryEnricher.Of(
            Entry("Some Shop", 12.00m, memo: "VISA Debitkartenumsatz vom 16.10.2026"),
            Mail(vendor: "Some Shop")
        );

        StringAssert.Contains(enriched.Memo, "VISA Debitkartenumsatz vom 16.10.2026");
    }

    [TestMethod]
    public void TheSourceClosesTheMemo()
    {
        var enriched = EntryEnricher.Of(
            Entry("ISSUER", 12.00m, memo: "Card payment"),
            Mail(vendor: "Kleinanzeigen")
        );

        StringAssert.EndsWith(enriched.Memo, "via kleinanzeigen");
    }

    [TestMethod]
    public void ATitleTheShopCutShortGivesWayToTheWholeOne()
    {
        // Amazon shortens the title where it has no room and prints it whole further down; a memo
        // that lists both says the same thing twice and once badly.
        var mail = Mail(
            vendor: "Amazon",
            orderNumbers: ["000-0000000-0000001"],
            fields: new Dictionary<string, string>
            {
                ["items"] =
                    "Beispiel Magnetic Child...\nBeispiel Magnetische Kindersicherung 20 Schlösser",
            }
        );

        var enriched = EntryEnricher.Of(Entry("AMZN Mktp DE", 32.99m), mail);

        StringAssert.StartsWith(
            enriched.Memo,
            "Order 000-0000000-0000001 · Beispiel Magnetische Kindersicherung 20 Schlösser"
        );
        Assert.IsFalse(enriched.Memo.Contains("Child...", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ALongMemoIsCutRatherThanGrowingWithoutBound()
    {
        var entry = Entry("Some Shop", 12.00m, memo: new string('x', 500));

        var enriched = EntryEnricher.Of(entry, mail: null);

        Assert.AreEqual(EntryEnricher.MaxMemoLength, enriched.Memo.Length);
    }

    [TestMethod]
    public void TheShopTheMailNamesBeatsTheLegalEntityTheBankPrints()
    {
        // A row that says "AUDIO PARTNER s.r.o." categorizes nothing; the receipt knows it is
        // the music shop, and the order number leads what is left of the memo.
        var mail = Mail(
            vendor: "Kytary",
            orderNumbers: ["OBJ20000001"],
            fields: new Dictionary<string, string> { ["merchant"] = "Kytary" }
        );

        var enriched = EntryEnricher.Of(
            Entry("AUDIO PARTNER s.r.o.", 137.40m, memo: "AUDIO PARTNER s.r.o. OBJ20000001"),
            mail
        );

        Assert.AreEqual("Kytary", enriched.Payee);
        Assert.AreEqual("Order OBJ20000001 · AUDIO PARTNER s.r.o.", enriched.Memo);
    }

    private static string First(EnrichedEntry enriched) => enriched.Memo.Split(" · ")[0];

    private static CorrelationEntry Entry(string payee, decimal amount, string memo = "") =>
        new(0, Purchased, payee, amount, memo);

    private static MailFacts Mail(
        string vendor,
        IReadOnlyList<string>? orderNumbers = null,
        IReadOnlyDictionary<string, string>? fields = null
    ) =>
        new(
            new MailRecord(
                "message.emlx",
                Received,
                "Your order",
                $"service@{vendor}.de",
                vendor,
                "body"
            ),
            vendor,
            orderNumbers ?? [],
            [],
            fields ?? new Dictionary<string, string>()
        );
}

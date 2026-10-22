using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

/// <summary>
/// The shipped patterns against what the shops really send. Every body below is a line taken from
/// a real message, kept exactly as it arrives — direction marks and missing spaces included.
/// </summary>
[TestClass]
public class ShippedRulesTest
{
    [TestMethod]
    public void AThomannOrderNumberKeepsItsDottedPart()
    {
        var facts = FactsOf(
            "kundenservice@thomann.de",
            "Ihre Bestellung bei Thomann (123456)",
            "Kundennummer123456AuftragsartNormalauftragAuftragsnummer100000.000001Auftragsdatum30.09.2026"
        );

        Assert.AreEqual("100000.000001", facts.Fields["orderNumber"]);
    }

    [TestMethod]
    public void AThomannTotalIsTheSumNotTheVatOrTheCustomerNumber()
    {
        var facts = FactsOf(
            "kundenservice@thomann.de",
            "Ihre Bestellung bei Thomann (123456)",
            "Warenwert193,80 € Nettobetrag162,86 €19% MwSt.30,94 € **Gesamtbetrag****193,80 €**"
        );

        Assert.AreEqual("193,80", facts.Fields["total"]);
    }

    [TestMethod]
    public void AnAmazonOrderNumberSurvivesItsDirectionMark()
    {
        var facts = FactsOf(
            "ship-confirm@amazon.de",
            "Thanks for your order",
            "Order # \u200F028-1234567-8901234"
        );

        CollectionAssert.Contains(facts.OrderNumbers.ToArray(), "028-1234567-8901234");
    }

    [TestMethod]
    public void AnAmazonTotalArrivesWithoutASpace()
    {
        var facts = FactsOf("ship-confirm@amazon.de", "Thanks for your order", "Total€34.99");

        Assert.AreEqual("34.99", facts.Fields["total"]);
    }

    [TestMethod]
    public void AnAmazonConfirmationListsTheItemsTheSellerAndConditionName()
    {
        var facts = FactsOf(
            "ship-confirm@amazon.de",
            "Thanks for your order",
            "Order # \u200F000-0000000-0000001\n\n"
                + "Beispiel Kabelbinder 60 Stück\n\n"
                + "Sold by Beispiel Versand\n\nCondition: New\n\nQuantity: 1\n\n€13,99\n\n"
                + "Beispiel Zugfeder-Sortiment\n\n"
                + "Sold by Beispiel Handel\n\nCondition: New\n\nQuantity: 1\n\n€6,59\n\n"
                + "Total€20,58"
        );

        CollectionAssert.AreEqual(
            new[] { "Beispiel Kabelbinder 60 Stück", "Beispiel Zugfeder-Sortiment" },
            facts.Items.ToArray()
        );
    }

    [TestMethod]
    public void ADeliveryNoteListsTheTitlesThatArrivedAndNotItsOwnProse()
    {
        var facts = FactsOf(
            "shipment-tracking@amazon.de",
            "Your package was delivered",
            "Your package was delivered!\n\n"
                + "Delivered today\n\n"
                + "Parcel was handed to resident.\n\n"
                + "Alex – Musterstadt\n\n"
                + "Order # \u200F000-0000000-0000001\n\n"
                + "Beispiel Kabelbinder 60 Stück\n\n"
                + "Beispiel Zugfeder-Sortiment\n\n"
                + "Return or replace items in Your Orders.\n\n"
                + "Your Orders https://www.amazon.de/gp/css/order-history?ref_=fed_yo_default, "
                + "Your Account https://www.amazon.de/your-account?ref_=fed_ya_default\n\n"
                + "Amazon.de is a trading name for Amazon EU Sarl."
        );

        CollectionAssert.Contains(facts.OrderNumbers.ToArray(), "000-0000000-0000001");
        CollectionAssert.AreEqual(
            new[] { "Beispiel Kabelbinder 60 Stück", "Beispiel Zugfeder-Sortiment" },
            facts.Items.ToArray()
        );
    }

    [TestMethod]
    public void APaypalReceiptNamesTheShopAndTheCodeOfThePayment()
    {
        // The bank prints "1000000000001", which finds a mailbox of PayPal mail and no payment;
        // the code on the receipt finds the one payment.
        var facts = FactsOf(
            "service@paypal.de",
            "Thomann GmbH: 193,80 \u20ac EUR",
            "Hallo, Alex BeispielSie haben 193,80 \u20ac EUR an Thomann GmbH autorisiert\n\n"
                + "H\u00e4ndlerThomann GmbH\ninfo@thomann.deTransaktionsdatum30.09.2026\n\n"
                + "Transaktionscode: 36C3079748428250K"
        );

        Assert.AreEqual("Thomann GmbH", facts.Fields["merchant"]);
        Assert.AreEqual("36C3079748428250K", facts.Fields["transactionId"]);
    }

    [TestMethod]
    public void APaypalReceiptForAnAuctionHouseIsReadToo()
    {
        var facts = FactsOf(
            "service@paypal.de",
            "eBay S.a.r.l.: 19,81 \u20ac EUR",
            "Sie haben 19,81 \u20ac EUR an eBay S.a.r.l. autorisiert\n\n"
                + "H\u00e4ndlereBay S.a.r.l.\nTransaktionscode: 6WB65010FJ786652D"
        );

        Assert.AreEqual("eBay S.a.r.l.", facts.Fields["merchant"]);
        Assert.AreEqual("6WB65010FJ786652D", facts.Fields["transactionId"]);
    }

    [TestMethod]
    public void MoneyThatOnlyWentToPaypalStillCarriesItsCode()
    {
        // A deposit from the bank account: PayPal is the only party there is, so the code is the
        // one thing the mail adds.
        var facts = FactsOf(
            "service@paypal.de",
            "Sie haben 58,56 \u20ac EUR auf Ihr PayPal-Konto eingezahlt",
            "Hallo Alex Beispiel\n"
                + "Sie haben 58,56 \u20ac EUR auf Ihr PayPal-Konto eingezahlt.\n\n"
                + "Transaktionsdetails\n\nTransaktionscode\n\n8MV091879G771883W\n\n"
                + "Transaktionsdatum\n\n27. M\u00e4rz 2026\n\nGeld eingezahlt von:\n\n"
                + "Deutsche Kreditbank AG0000Konto-ID"
        );

        Assert.AreEqual("8MV091879G771883W", facts.Fields["transactionId"]);
        Assert.IsFalse(facts.Fields.ContainsKey("recipient"), "nobody was paid");
    }

    [TestMethod]
    public void MoneyPaypalItselfSentStillCarriesItsCode()
    {
        // The other half of the same transfer: PayPal, named as the sender, and a code whose
        // amount is written before the number.
        var facts = FactsOf(
            "service@paypal.de",
            "PayPal (Europe) S. \u00e0 r.l. et Cie, S.C.A. hat Ihnen gerade \u20ac58,56 EUR mit PayPal gesendet.",
            "**Zahlungsdetails**\n\n**Betrag:**\u20ac58,56 EUR"
                + "**Transaktionsdatum:**31. M\u00e4rz 2026"
                + "**Transaktionscode:**5R987488J7860704WSie k\u00f6nnen die Details"
        );

        Assert.AreEqual("5R987488J7860704W", facts.Fields["transactionId"]);
    }

    [TestMethod]
    public void MoneySentToAPersonNamesThePersonAndTheCode()
    {
        // A payment to a person: the bank row says PayPal and nothing else, so the name and the
        // code that answers a question at PayPal have to come from the mail.
        var facts = FactsOf(
            "service@paypal.de",
            "Du hast eine Zahlung gesendet",
            "Hallo Alex Beispiel\n"
                + "Du hast 65,00 \u20ac EUR an Jan Beispiel gesendet\n\n"
                + "Transaktionsdetails\n\nTransaktionscode\n\n0W702662EH627104L\n\n"
                + "Transaktionsdatum\n\n14. Juni 2026\n\n"
                + "Geld gesendet65,00 \u20ac EURGeb\u00fchr0,00 \u20ac EURGezahlt mit:"
        );

        Assert.AreEqual("Jan Beispiel", facts.Fields["recipient"]);
        Assert.AreEqual("0W702662EH627104L", facts.Fields["transactionId"]);
    }

    [TestMethod]
    public void MoneyAPersonSentSaysWhoSentIt()
    {
        var facts = FactsOf(
            "service@paypal.de",
            "Du hast eine Zahlung erhalten",
            "Ralf Beispiel hat dir 85,00 \u20ac EUR gesendet\n\n"
                + "Transaktionscode: 5YH614718D462300X"
        );

        Assert.AreEqual("Ralf Beispiel", facts.Fields["merchant"]);
        Assert.AreEqual("5YH614718D462300X", facts.Fields["transactionId"]);
    }

    [TestMethod]
    public void MoneyAPersonWasPaidSaysWhoWasPaidEvenInLowerCase()
    {
        var facts = FactsOf(
            "service@paypal.de",
            "Du hast eine Zahlung gesendet",
            "Sie haben 100,00 \u20ac EUR an nicky beispiel gezahlt\n\n"
                + "Transaktionscode: 6U232167L77721709"
        );

        Assert.AreEqual("nicky beispiel", facts.Fields["recipient"]);
        Assert.AreEqual("6U232167L77721709", facts.Fields["transactionId"]);
    }

    [TestMethod]
    public void AnEbayConfirmationListsWhatWasBought()
    {
        var facts = FactsOf(
            "ebay@ebay.de",
            "Vielen Dank! Ihre Bestellung wurde best\u00e4tigt.",
            "Beispiel Bonbons zuckerfrei\n\nPreis:\nEUR 5,49\n\n"
                + "Artikelnr.:\n000000000000\n\nBestellnummer:\n00-00000-00000\n\n"
                + "Verk\u00e4ufer:\nbeispiel_verkaeufer\n\n"
                + "Beispiel Bonbons Tropisch 198g\n\nPreis:\nEUR 4,72\n\n"
                + "Die Widerrufsbelehrung finden Sie im letzten Abschnitt"
        );

        CollectionAssert.AreEqual(
            new[]
            {
                "Beispiel Bonbons zuckerfrei",
                "Beispiel Bonbons Tropisch 198g",
            },
            facts.Items.ToArray()
        );
        Assert.AreEqual("00-00000-00000", facts.Fields["orderNumber"]);
    }

    [TestMethod]
    public void ADeliveryNoteSaysWhatArrivedEvenWhenItsFooterHasNoFullStops()
    {
        var facts = FactsOf(
            "shipment-tracking@amazon.de",
            "Delivered: \u2018Beispiel Federn-Set...\u2019 and 1 more item",
            "Order # \u200F000-0000000-0000001\n\n"
                + "Return or replace items in Your Orders\n\n"
                + "Privacy Notice https://www.amazon.de/gp/help/customer/display.html?nodeId=100000000"
        );

        CollectionAssert.AreEqual(new[] { "Beispiel Federn-Set..." }, facts.Items.ToArray());
    }

    [TestMethod]
    public void OneMailHasOneListOfItemsEvenWhenTwoShapesWouldReadIt()
    {
        var facts = FactsOf(
            "ship-confirm@amazon.de",
            "Ihre Amazon.de Bestellung",
            "3 x Kabel 3,50 €\n\n"
                + "Beispiel Kabelbinder 60 Stück\n\n"
                + "Beispiel Zugfeder-Sortiment"
        );

        CollectionAssert.AreEqual(new[] { "Kabel 3,50 €" }, facts.Items.ToArray());
    }

    [TestMethod]
    public void APaypalPaymentOfABahnTicketYieldsTheMerchantAndTheAmount()
    {
        var facts = FactsOf(
            "service@paypal.de",
            "Sie haben eine Zahlung gesendet",
            "Sie haben 72,98 € EUR an DB Vertrieb GmbH gezahlt. HändlerDB Vertrieb GmbH"
        );

        Assert.AreEqual("PayPal", facts.Vendor);
        Assert.AreEqual("DB Vertrieb GmbH", facts.Fields["merchant"]);
        CollectionAssert.Contains(facts.Amounts.ToArray(), 72.98m);
    }

    [TestMethod]
    public void APaypalReceiptYieldsItsTransactionCodeAndTheBestellnummer()
    {
        var facts = FactsOf(
            "service@paypal.de",
            "Receipt for your payment",
            "Transaktionsdatum19.10.2026Bestellnummer100000000001 "
                + "Transaktionscode: 8B170000EK0000001"
        );

        Assert.AreEqual("8B170000EK0000001", facts.Fields["transactionId"]);
        Assert.AreEqual("100000000001", facts.Fields["orderNumber"]);
        CollectionAssert.Contains(facts.OrderNumbers.ToArray(), "100000000001");
    }

    [TestMethod]
    public void ADbBookingYieldsTheOrderNumberThePaypalReceiptAlsoQuotes()
    {
        var facts = FactsOf(
            "noreply@bahn.de",
            "Ihre Buchungsbestätigung",
            "Am 19.10.2026 um 13:04 Uhr wurde der Auftrag mit der Auftragsnummer "
                + "100000000001 wie folgt gebucht:"
        );

        Assert.AreEqual("Deutsche Bahn", facts.Vendor);
        Assert.AreEqual("100000000001", facts.Fields["orderNumber"]);
    }

    [TestMethod]
    public void ADbBookingListsTheFaresItBooked()
    {
        var facts = FactsOf(
            "noreply@bahn.de",
            "Ihre Buchungsbestätigung",
            "Super Sparpreis Senior, 2. Klasse\n"
                + "von Berlin Hbf, 23.10.2026 20:03 Uhr\n"
                + "Sitzplatzreservierung, 2. Klasse, ICE 1107\n"
                + "Gesamtbetrag:72,98 EUR"
        );

        Assert.AreEqual(
            "Super Sparpreis Senior, 2. Klasse\nSitzplatzreservierung, 2. Klasse, ICE 1107",
            facts.Fields["items"]
        );
        Assert.AreEqual("72,98", facts.Fields["total"]);
    }

    [TestMethod]
    public void AKleinanzeigenPurchaseNamesTheItemItIsAbout()
    {
        var facts = FactsOf(
            "noreply@kleinanzeigen.de",
            "Dein Kauf wurde bestätigt!",
            "Transaktion ID: 193d8050-0000-11f1-0000-000000000000\n\n"
                + "Der Verkäufer hat deinen Kauf des Artikels **\"Beispielartikel mit "
                + "Klemme\"** über \"Direkt kaufen\" bestätigt."
        );

        Assert.AreEqual("Kleinanzeigen", facts.Vendor);
        Assert.AreEqual("193d8050-0000-11f1-0000-000000000000", facts.Fields["transactionId"]);
        Assert.AreEqual(
            "Beispielartikel mit Klemme",
            facts.Items.Single()
        );
    }

    [TestMethod]
    public void AKleinanzeigenPaymentNamesTheItemInGermanQuotes()
    {
        var facts = FactsOf(
            "noreply@kleinanzeigen.de",
            "Zahlung erfolgreich",
            "Der nächste Schritt: Der Verkäufer wird den Artikel "
                + "**„Beispiel-Effektgeraet\"** nun versenden."
        );

        Assert.AreEqual("Beispiel-Effektgeraet", facts.Items.Single());
    }

    [TestMethod]
    public void AKleinanzeigenServiceOrderYieldsItsBestellnummerAndSum()
    {
        var facts = FactsOf(
            "noreply@kleinanzeigen.de",
            "Bestellbestätigung",
            "Gesamt: 6,49 €Bestellnummer: 10000000\nBestelldatum: 13.08.2026\nSumme: 6,49 €"
        );

        Assert.AreEqual("10000000", facts.Fields["orderNumber"]);
        Assert.AreEqual("6,49", facts.Fields["total"]);
    }

    [TestMethod]
    public void AnAmazonShippingMailListsTheWholeTitleOfEveryItemInTheParcel()
    {
        // Amazon's own subject shortens the first title and forgets the second; the body carries
        // both in full — as the description of each product picture, beside the shortened link.
        var facts = FactsOfHtml(
            "ship-confirm@amazon.de",
            "Ihre Amazon.de Bestellung von Beispiel Federn-Set...",
            "<div>Order # \u200F000-0000000-0000001</div>"
                + ParcelItem(
                    "Beispiel Federn-Set 12 Größen Edelstahl für Maschinenbau",
                    "Beispiel Versand"
                )
                + ParcelItem(
                    "Beispiel Spannfedern 10 Stück Edelstahl mit Doppelhaken",
                    "Beispiel Handel"
                )
        );

        CollectionAssert.AreEqual(
            new[]
            {
                "Beispiel Federn-Set 12 Größen Edelstahl für Maschinenbau",
                "Beispiel Spannfedern 10 Stück Edelstahl mit Doppelhaken",
            },
            facts.Items.ToArray()
        );
        CollectionAssert.Contains(facts.OrderNumbers.ToArray(), "000-0000000-0000001");
    }

    [TestMethod]
    public void AnAmazonParcelListNamesEveryItemInFullEvenWhenATitleWraps()
    {
        // The subject says "Beispiel Federn-Set..." and forgets the rest of the parcel; the
        // body lists both products, each title wrapped over two lines.
        var facts = FactsOf(
            "ship-confirm@amazon.de",
            "Ihre Amazon.de Bestellung von Beispiel Federn-Set...",
            "Thanks for your order!\n\nArriving Tuesday\n\nAlex – Musterstadt\n\nOrder #\n"
                + "\u200F000-0000000-0000001\n\n"
                + "* Beispiel Federn-Set 12 Größen\nEdelstahl für Maschinenbau\n"
                + "  Quantity: 1\n  13.99 EUR\n"
                + "* Beispiel Spannfedern 10 Stück Edelstahl mit Doppelhaken\n"
                + "  Quantity: 1\n  6.59 EUR"
        );

        CollectionAssert.AreEqual(
            new[]
            {
                "Beispiel Federn-Set 12 Größen Edelstahl für Maschinenbau",
                "Beispiel Spannfedern 10 Stück Edelstahl mit Doppelhaken",
            },
            facts.Items.ToArray()
        );
        CollectionAssert.Contains(facts.OrderNumbers.ToArray(), "000-0000000-0000001");
    }

    [TestMethod]
    public void AKytaryReceiptNamesTheShopTheBankOnlyPrintsAsItsLegalEntity()
    {
        // The bank writes "AUDIO PARTNER s.r.o."; only the mail says whose shop that is.
        var facts = FactsOf(
            "kommunikation@kytary.de",
            "Zahlung erhalten",
            "Sehr geehrter Kunde,\n"
                + "wir haben soeben Ihre 137,40 € Zahlung erhalten OBJ20000001.\n\n"
                + "Vielen Dank.\n\nAUDIO PARTNER s.r.o.\nKytary.de\nKundenhotline: 089 370 401 12"
        );

        Assert.AreEqual("Kytary", facts.Vendor);
        Assert.AreEqual("Kytary", facts.Fields["merchant"]);
        Assert.AreEqual("OBJ20000001", facts.Fields["orderNumber"]);
        Assert.AreEqual("137,40", facts.Fields["total"]);
    }

    [TestMethod]
    public void AKytaryConfirmationQuotesTheOrderNumberItsReceiptQuotes()
    {
        // The confirmation names no action in its subject that a rule could rely on, and the
        // goods themselves are in the attached PDF; the number is what ties the two mails
        // together and what the row is searched by.
        var facts = FactsOf(
            "kommunikation@kytary.de",
            "Ihre Bestellung",
            "vielen Dank für Ihre Bestellung OBJ20000001 vom 27.03.2026 15:39 im e-Shop "
                + "Kytary.de.\n\nDie Kopie der Bestellung finden Sie in der Anlage (PDF).\n\n"
                + "https://kytary.de/Order/Order/?number=OBJ20000001-92210081082B871F7151CBA3"
        );

        CollectionAssert.Contains(facts.OrderNumbers.ToArray(), "OBJ20000001");
        Assert.AreEqual("Kytary", facts.Fields["merchant"]);
    }

    [TestMethod]
    public void APaypalCheckoutNamesTheShopUnderZahlungAnAndNotTheSentenceAroundIt()
    {
        // The receipt talks about itself — "wenn der Händler Ihre Bestellung abgewickelt hat" —
        // before it names the shop on the line below "Zahlung an". Only the second is a payee.
        var facts = FactsOf(
            "service@paypal.de",
            "Ihre Bestellung bei Beispiel Airlines S.A.",
            "Alex Beispiel, danke, dass Sie mit PayPal gezahlt haben\n\n"
                + "Hier sind die Details zu Ihrem Einkauf. Wir setzen uns ggf. mit Ihnen in "
                + "Verbindung, wenn der Händler Ihre Bestellung abgewickelt hat.\n\n"
                + "Details Ihres Einkaufs\n\nZahlung von\n\nAlex Beispiel\n\nZahlung an\n\n"
                + "Beispiel Airlines S.A.\n\nTransaktionscode\n74U00000NW000000G"
        );

        Assert.AreEqual("Beispiel Airlines S.A.", facts.Fields["merchant"]);
        Assert.AreEqual("74U00000NW000000G", facts.Fields["transactionId"]);
    }

    [TestMethod]
    public void AnAmazonDigestCarriesEveryOrderAndEveryTotalItAnnounces()
    {
        // One confirmation for two orders: the amounts are what a card row can be matched by, so
        // both totals have to survive, not just the first order's.
        var facts = FactsOf(
            "ship-confirm@amazon.de",
            "Thanks for your order",
            "Thanks for your order!\n\nOrdered\nDispatched\nOut for delivery\nDelivered\n\n"
                + "Arriving tomorrow 7 am – 11 am\n\nAlex – Musterstadt\n"
                + "Order # \u200F000-0000000-0000001\nView or edit order\n\n"
                + Parcel("Beispiel Gummiringe 100 Stück, stark, breit", "6,99")
                + Parcel("Beispiel Gummiringe 100 Stück, dünn", "5,49")
                + "Total€27,42\n\nArriving Tuesday\n\nAlex – Musterstadt\n"
                + "Order # \u200F000-0000000-0000002\nView or edit order\n\n"
                + Parcel("Beispiel Kleinbox mit Deckel, 12 Stück", "8,59")
                + "Total€33,95\n\nAmazon.de is a trading name for Amazon EU Sarl."
        );

        CollectionAssert.AreEqual(
            new[] { "000-0000000-0000001", "000-0000000-0000002" },
            facts.OrderNumbers.ToArray()
        );
        CollectionAssert.IsSubsetOf(new[] { 27.42m, 33.95m }, facts.Amounts.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "Beispiel Gummiringe 100 Stück, stark, breit",
                "Beispiel Gummiringe 100 Stück, dünn",
                "Beispiel Kleinbox mit Deckel, 12 Stück",
            },
            facts.Items.ToArray()
        );
    }

    /// <summary>One product of an order confirmation: title, seller, condition, quantity, price.</summary>
    private static string Parcel(string title, string price) =>
        $"{title}\nSold by Beispiel Versand\nCondition: New\nQuantity: 1\n€{price}\n\n";

    /// <summary>One parcel product as Amazon lays it out: picture, shortened link, seller.</summary>
    private static string ParcelItem(string title, string seller) =>
        "<table><tr>"
        + "<td><a href='https://www.amazon.de/dp/B0X'><img src='x.jpg' alt='"
        + title
        + "'></a></td>"
        + "<td><a href='https://www.amazon.de/dp/B0X'>"
        + title[..30]
        + "...</a>"
        + "<div>Sold by "
        + seller
        + "</div><div>Condition: New</div><div>Quantity: 1</div></td>"
        + "</tr></table>";

    private static MailFacts FactsOfHtml(string from, string subject, string html) =>
        FactsOf(from, subject, HtmlToText.Convert(html));

    private static MailFacts FactsOf(string from, string subject, string body) =>
        SignalExtractor.Extract(Mail(subject, from, body), RuleSetLoader.Shipped());
    private static MailRecord Mail(string subject, string from, string body) =>
        new("/mail/message.emlx", new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero),
            subject, from, string.Empty, body, "302.1@example.com");
}

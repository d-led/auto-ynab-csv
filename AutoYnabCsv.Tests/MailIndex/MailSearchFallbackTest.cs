using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Enrichment;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class MailSearchFallbackTest
{
    private static readonly DateOnly Day = new(2026, 10, 2);
    private static readonly DateTimeOffset Received = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AnEntryTheIndexCannotExplainGetsSearchedFor()
    {
        var finder = new FakeFinder(("Thomann GmbH", "/mail/thomann.emlx"));

        var matches = Search(Entry("PAYPAL *THOMANN", 177.29m), finder, found: []);

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual("Thomann", matches[0].Mail.Vendor);
    }

    [TestMethod]
    public void AFoundMailStillHasToFitTheAmountAndTheDay()
    {
        var finder = new FakeFinder(("Thomann GmbH", "/mail/thomann.emlx"));

        var matches = Search(Entry("PAYPAL *THOMANN", 12.00m), finder, found: []);

        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void AnEntryWithAnEvidencedMailIsNotSearchedFor()
    {
        var finder = new FakeFinder(("Thomann GmbH", "/mail/thomann.emlx"));
        var entry = Entry("PAYPAL *THOMANN", 177.29m);
        var known = new Match(
            entry,
            Facts("/mail/indexed.emlx"),
            0,
            new Evidence(EvidenceKind.AmountAndCounterparty, "177.29 · Thomann named")
        );

        var matches = Search(entry, finder, [known]);

        Assert.AreEqual(0, matches.Count);
        Assert.AreEqual(0, finder.Queries.Count);
    }

    [TestMethod]
    public void WhatTheSearchReadReplacesTheHeaderOnlyCopyOfTheSameMessage()
    {
        var entry = Entry("PAYPAL *THOMANN 028-1234567-8901234", 177.29m);
        var stub = Facts("/mail/stub.emlx") with
        {
            Mail = Facts("/mail/stub.emlx").Mail with { Text = string.Empty },
        };
        var read = stub with
        {
            Mail = stub.Mail with { Text = "Kabel 3,50 €" },
            Fields = new Dictionary<string, string> { ["items"] = "Kabel 3,50 €" },
        };
        var search = new MailSearchResult(
            [new Match(entry, read, 0, new Evidence(EvidenceKind.Reference, "028-1234567-8901234"))],
            1,
            0,
            1
        );

        var facts = search.Replacing([stub]);

        Assert.AreEqual("Kabel 3,50 €", facts.Single().Items.Single());
    }

    [TestMethod]
    public void ARowThatNamesItsOwnShopIsNotReadFromMail()
    {
        var shop = Entry("Thomann GmbH", 177.29m);
        var stub = Facts("/mail/stub.emlx") with
        {
            Mail = Facts("/mail/stub.emlx").Mail with { Text = string.Empty },
        };
        var known = new Match(
            shop,
            stub,
            0,
            new Evidence(EvidenceKind.Reference, "12345678")
        );
        var read = new List<string>();
        var where = new MailLookup(
            Read,
            facts =>
            {
                read.Add(facts.Mail.Path);
                return facts;
            }
        );

        var result = MailSearchFallback.Search(
            [],
            [known],
            new FakeFinder(),
            where,
            MailSearchFallback.QueryOf
        );

        Assert.AreEqual(0, read.Count);
        Assert.AreEqual(0, result.Matches.Count);
    }

    [TestMethod]
    public void OnlyTheClosestFittingMailIsRead()
    {
        // An order number appears in the confirmation, the dispatch note and the delivery note.
        // Asking Mail for all of them is how a mailbox stops responding.
        var finder = new FakeFinder(
            ("PAYPAL *THOMANN 028-1234567-8901234", "/mail/confirmation.emlx"),
            ("PAYPAL *THOMANN 028-1234567-8901234", "/mail/delivered.emlx")
        );
        var fetched = new List<string>();
        var where = new MailLookup(Read, facts =>
        {
            fetched.Add(facts.Mail.Path);
            return facts;
        });

        var entry = Entry("PAYPAL *THOMANN 028-1234567-8901234", 177.29m);

        MailSearchFallback.Search(
            [entry],
            [],
            finder,
            where,
            MailSearchFallback.QueryOf
        );

        Assert.AreEqual(1, fetched.Count);
    }

    [TestMethod]
    public void TheSameMerchantIsSearchedForOnce()
    {
        var finder = new FakeFinder(("PayPal Europe S.a.r.l.", "/mail/paypal.emlx"));
        var entries = new[]
        {
            Entry("PayPal Europe S.a.r.l.", 177.29m),
            Entry("PayPal Europe S.a.r.l.", 12.00m),
        };

        MailSearchFallback.Search(
            entries,
            [],
            finder,
            MailLookup.Local(Read),
            MailSearchFallback.QueryOf
        );

        Assert.AreEqual(1, finder.Queries.Count);
    }

    [TestMethod]
    public void ABodyIsFetchedOnlyForACandidateTheDayAllows()
    {
        var finder = new FakeFinder(
            ("Thomann GmbH", "/mail/far.emlx"),
            ("Thomann GmbH", "/mail/near.emlx")
        );
        var fetched = new List<string>();
        var lookup = new MailLookup(Read, facts =>
        {
            fetched.Add(facts.Mail.Path);
            return facts;
        });

        var result = SearchWith(Entry("PAYPAL *THOMANN", 177.29m), finder, lookup);

        CollectionAssert.AreEqual(new[] { "/mail/near.emlx" }, fetched);
        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual(1, result.Entries);
        Assert.AreEqual(1, result.Candidates);
    }

    [TestMethod]
    public void TheQueryCarriesTheTransferNumberTheMerchantAndTheDay()
    {
        var query = MailSearchFallback.QueryOf(
            new CorrelationEntry(
                0,
                Day,
                "PayPal Europe S.a.r.l.",
                177.29m,
                "1050000000001/. Thomann GmbH, Ihr Einkauf bei Thomann GmbH"
            )
        );

        CollectionAssert.AreEqual(new[] { "1050000000001" }, query.Identifiers.ToArray());
        Assert.AreEqual("Thomann GmbH", query.Phrase);
        Assert.AreEqual(Day, query.Day);
        Assert.AreEqual(Correlator.SearchWindowDays, query.WindowDays);
    }

    [TestMethod]
    public void TheReceiptNamesTheShopAndTheShopSaysWhatWasBought()
    {
        // The bank only says PayPal, and PayPal's receipt only says who was paid. The shop's own
        // mail is the one that names the goods, and the receipt is what makes it the same purchase.
        var finder = new FakeFinder(
            ("PayPal Europe S.a.r.l.", "/mail/receipt.emlx"),
            ("Thomann GmbH", "/mail/thomann.emlx")
        );
        var receipt = Facts("/mail/receipt.emlx") with
        {
            Vendor = "PayPal",
            Fields = new Dictionary<string, string> { ["merchant"] = "Thomann GmbH" },
        };
        var shop = Facts("/mail/thomann.emlx") with
        {
            Fields = new Dictionary<string, string>
            {
                ["merchant"] = "Thomann GmbH",
                ["items"] = "Kabel 3,50 €\nSaitensatz 12,50 €",
            },
        };
        var where = new MailLookup(
            path => path.Contains("receipt", StringComparison.Ordinal) ? receipt : shop,
            facts => facts
        );

        var result = SearchWith(Entry("PayPal Europe S.a.r.l.", 177.29m), finder, where);
        var mails = result.Replacing([]);

        Assert.AreEqual(2, result.Matches.Count);
        CollectionAssert.Contains(mails.Select(mail => mail.Mail.Path).ToArray(), "/mail/thomann.emlx");
        StringAssert.Contains(
            result.Matches.Single(match => match.Mail.Mail.Path == "/mail/thomann.emlx").Evidence.Describe(),
            "Thomann GmbH named by PayPal"
        );
    }

    [TestMethod]
    public void AnAuctionHouseReceiptLeadsToTheGoodsAndTheOrderNumber()
    {
        // The row says only PayPal and 19,81 €. PayPal's receipt names the shop, and the shop's
        // confirmation names what was bought: both mails end up in one memo.
        var finder = new FakeFinder(("PayPal Europe S.a.r.l.", "/mail/receipt.emlx"), ("eBay", "/mail/ebay.emlx"));
        var receipt = Facts("/mail/receipt.emlx") with
        {
            Vendor = "PayPal",
            Amounts = [19.81m],
            OrderNumbers = [],
            Fields = new Dictionary<string, string>
            {
                ["merchant"] = "eBay S.a.r.l.",
                ["transactionId"] = "6WB65010FJ786652D",
            },
        };
        var order = Facts("/mail/ebay.emlx") with
        {
            Vendor = "eBay",
            Amounts = [19.81m],
            OrderNumbers = ["00-00000-00000"],
            Fields = new Dictionary<string, string>
            {
                ["items"] =
                    "Beispiel Bonbons zuckerfrei\n"
                    + "Beispiel Bonbons Tropisch 198g",
            },
        };
        var where = new MailLookup(
            path => path.Contains("receipt", StringComparison.Ordinal) ? receipt : order,
            facts => facts
        );
        var row = Entry("PayPal Europe S.a.r.l.", 19.81m);

        var result = SearchWith(row, finder, where);
        var enriched = EntryEnricher.Of(row, result.Replacing([]));

        Assert.AreEqual("eBay S.a.r.l.", enriched.Payee);
        Assert.AreEqual(
            "Order 00-00000-00000 · Transaktion 6WB65010FJ786652D · "
                + "Beispiel Bonbons zuckerfrei · Beispiel Bonbons Tropisch 198g "
                + "· via paypal",
            enriched.Memo
        );
    }

    [TestMethod]
    public void AShopTheRowAlreadyNamesIsNotSearchedForTwice()
    {
        var finder = new FakeFinder(("PAYPAL *THOMANN", "/mail/receipt.emlx"));
        var receipt = Facts("/mail/receipt.emlx") with
        {
            Vendor = "PayPal",
            Fields = new Dictionary<string, string> { ["merchant"] = "Thomann GmbH" },
        };

        var result = SearchWith(
            Entry("PAYPAL *THOMANN", 177.29m),
            finder,
            new MailLookup(_ => receipt, facts => facts)
        );

        Assert.AreEqual(1, finder.Queries.Count);
        Assert.AreEqual(1, result.Matches.Count);
    }

    [TestMethod]
    public void TheClueIsTheMerchantTheEntryNames()
    {
        var clue = MailSearchFallback.ClueOf(
            new CorrelationEntry(
                0,
                Day,
                "PayPal Europe S.a.r.l.",
                177.29m,
                "1050000000001/. Thomann GmbH, Ihr Einkauf bei Thomann GmbH"
            )
        );

        Assert.AreEqual("Thomann GmbH", clue);
    }

    [TestMethod]
    public void AnAmazonOrderNumberOnTheBankRowIsSearchedFor()
    {
        var query = MailSearchFallback.QueryOf(Entry("AMAZON.DE 028-1234567-8901234", 34.99m));

        CollectionAssert.AreEqual(new[] { "028-1234567-8901234" }, query.Identifiers.ToArray());
    }

    [TestMethod]
    public void APaypalTransactionCodeOnTheBankRowIsSearchedFor()
    {
        var query = MailSearchFallback.QueryOf(Entry("PAYPAL 8B170000EK0000001", 72.98m));

        CollectionAssert.AreEqual(new[] { "8B170000EK0000001" }, query.Identifiers.ToArray());
    }

    [TestMethod]
    public void TheSearchSaysWhenItHappenedAndWhatItLooksFor()
    {
        var reported = new List<EntrySearch>();
        var finder = new FakeFinder(("Thomann GmbH", "/mail/thomann.emlx"));
        var entry = new CorrelationEntry(
            0,
            Day,
            "PayPal Europe S.a.r.l.",
            177.29m,
            "1050000000001/. Thomann GmbH, Ihr Einkauf bei Thomann GmbH"
        );

        MailSearchFallback.Search(
            [entry],
            [],
            finder,
            MailLookup.Local(Read),
            MailSearchFallback.QueryOf,
            reported.Add
        );

        Assert.AreEqual(1, reported.Count);
        StringAssert.Contains(reported[0].Describe(), "2026-10-02");
        StringAssert.Contains(reported[0].Describe(), "177.29");
        StringAssert.Contains(reported[0].Describe(), "\"1050000000001\"");
        StringAssert.Contains(reported[0].Describe(), "\"Thomann GmbH\"");
    }

    private static IReadOnlyList<Match> Search(
        CorrelationEntry entry,
        IMailFinder finder,
        IReadOnlyList<Match> found
    ) =>
        MailSearchFallback
            .Search(
                [entry],
                found,
                finder,
                MailLookup.Local(Read),
                MailSearchFallback.QueryOf
            )
            .Matches;

    private static MailSearchResult SearchWith(
        CorrelationEntry entry,
        IMailFinder finder,
        MailLookup lookup
    ) => MailSearchFallback.Search([entry], [], finder, lookup, MailSearchFallback.QueryOf);

    private static MailFacts? Read(string path) =>
        Facts(path, path.Contains("far", StringComparison.Ordinal) ? Received.AddDays(-30) : Received);

    private static CorrelationEntry Entry(string payee, decimal amount) =>
        new(0, Day, payee, amount, string.Empty);

    private static MailFacts Facts(string path) =>
        Facts(path, Received);

    private static MailFacts Facts(string path, DateTimeOffset received) =>
        new(
            new MailRecord(
                path,
                received,
                "Ihre Thomann Bestellung",
                "order@thomann.de",
                "Thomann",
                "body",
                "302.1234567@thomann.de"
            ),
            "Thomann",
            ["12345678"],
            [177.29m],
            new Dictionary<string, string>()
        );

    private sealed class FakeFinder(params (string Clue, string Path)[] answers) : IMailFinder
    {
        public List<MailQuery> Queries { get; } = [];

        public IReadOnlyList<string> Find(MailQuery query, int limit = 10)
        {
            Queries.Add(query);

            return answers
                .Where(answer => answer.Clue.Contains(query.Phrase, StringComparison.OrdinalIgnoreCase))
                .Select(answer => answer.Path)
                .ToArray();
        }
    }
}

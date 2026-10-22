using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class RuleSetLoaderTest
{
    private static readonly DateTimeOffset Received = new(2024, 12, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AUserFileOverridesByRuleIdAndMayAddRules()
    {
        var path = WritePatterns(
            """
            {
              // a user override of a shipped rule
              "version": 1,
              "rules": [
                {
                  "id": "amazon.order",
                  "sender": "amazon\\.",
                  "vendor": "Amazon (mine)",
                  "fields": { "orderNumber": "\\b\\d{3}-\\d{7}-\\d{7}\\b" }
                },
                {
                  "id": "custom.invoice",
                  "subject": "Rechnung",
                  "vendor": "Utility",
                  "fields": { "invoiceNumber": "RECHNUNG\\s*(\\d+)" }
                }
              ]
            }
            """
        );

        try
        {
            var rules = RuleSetLoader.Load(path);

            CollectionAssert.Contains(rules.RuleIds.ToList(), "custom.invoice");
            Assert.AreEqual(
                "Amazon (mine)",
                rules.VendorOf(Mail("Bestellung 302-1234567-8901234", "order@amazon.de"))
            );
            Assert.AreEqual(
                "4711",
                rules.Extract(Mail("Ihre Rechnung", "billing@utility.example"))["invoiceNumber"]
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TheRulesAreShippedAsAFileInsideTheAssembly()
    {
        var shipped = RuleSetLoader.Shipped();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "amazon.order",
                "amazon.confirmation",
                "amazon.ordered",
                "amazon.titles",
                "paypal.payment",
                "paypal.received",
                "ebay.order",
                "thomann.order",
                "kytary.order",
                "db.booking",
                "kleinanzeigen.sale",
            },
            shipped.RuleIds.ToList()
        );
        StringAssert.Contains(RuleSetLoader.ShippedText(), "\"version\": 1");
    }

    [TestMethod]
    public void AUserFileWithOneIdLeavesTheOtherShippedRulesAlone()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "rules": [
                { "id": "amazon.order", "vendor": "Amazon (mine)", "fields": {} }
              ]
            }
            """
        );

        try
        {
            var rules = RuleSetLoader.Load(path);

            Assert.AreEqual("Amazon (mine)", rules.VendorOf(Mail("Bestellung", "order@amazon.de")));
            CollectionAssert.Contains(rules.RuleIds.ToList(), "db.booking");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TheSourcesAreTheFilesThatOverrideTheShippedRules()
    {
        var path = WritePatterns("""{ "version": 1, "rules": [] }""");

        try
        {
            CollectionAssert.AreEqual(new[] { path }, RuleSetLoader.Sources(path).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AUserFileMayNameASenderNoRuleMatches()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "vendors": { "kabelshop.example": "Kabelshop" }
            }
            """
        );

        try
        {
            var facts = SignalExtractor.Extract(
                Mail("Ihre Bestellung", "orders@kabelshop.example"),
                RuleSetLoader.Load(path)
            );

            Assert.AreEqual("Kabelshop", facts.Vendor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AUserFileMayRenameAShippedDomain()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "vendors": { "amazon.de": "Amazon (mine)" }
            }
            """
        );

        try
        {
            var facts = SignalExtractor.Extract(
                Mail("Newsletter", "news@amazon.de"),
                RuleSetLoader.Load(path)
            );

            Assert.AreEqual("Amazon (mine)", facts.Vendor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AUserFileMayTeachAShopTheItemsItLists()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "rules": [
                {
                  "id": "kabelshop.order",
                  "sender": "kabelshop\\.",
                  "vendor": "Kabelshop",
                  "fields": { "items": "(?m)^(?<item>\\d+ m .+)$" }
                }
              ]
            }
            """
        );

        try
        {
            var facts = SignalExtractor.Extract(
                Mail("Ihre Bestellung", "orders@kabelshop.example") with
                {
                    Text = "2 m Kabel 3,50 €\n1 m Litze 1,20 €",
                },
                RuleSetLoader.Load(path)
            );

            CollectionAssert.AreEqual(
                new[] { "2 m Kabel 3,50 €", "1 m Litze 1,20 €" },
                facts.Items.ToArray()
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void OnlyPayeesThatNameNothingAreWorthAMailboxSearch()
    {
        var rules = RuleSetLoader.Shipped();

        Assert.IsTrue(rules.NeedsMail("PayPal Europe S.a.r.l. et Cie S.C.A"));
        Assert.IsTrue(rules.NeedsMail("kleinanzeigen.de GmbH"));
        Assert.IsFalse(rules.NeedsMail("EDEKA"));
        Assert.IsFalse(rules.NeedsMail("SPC.Tierarztpraxis.Moos/Munchen"));
    }

    [TestMethod]
    public void AUserFileMayAddAPayeeThatIsWorthSearching()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "opaquePayees": ["kabelshop"]
            }
            """
        );

        try
        {
            Assert.IsTrue(RuleSetLoader.Load(path).NeedsMail("KABELSHOP ONLINE GMBH"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AddingAPayeeToSearchDoesNotRebuildTheIndex()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "opaquePayees": ["kabelshop"]
            }
            """
        );

        try
        {
            Assert.AreEqual(
                RuleSetLoader.Shipped().Fingerprint,
                RuleSetLoader.Load(path).Fingerprint
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AUserFileMayNameAPhraseThatMeansMoneyMoved()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "transfers": ["bargeld"]
            }
            """
        );

        try
        {
            var rules = RuleSetLoader.Load(path);

            Assert.IsTrue(rules.IsTransfer("Sparkasse", "Bargeld Auszahlung"));
            Assert.IsTrue(rules.IsTransfer("DKB", "KREDITKARTENABRECHNUNG"));
            Assert.IsFalse(rules.IsTransfer("REWE", "Einkauf"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AddingATransferPhraseDoesNotRebuildTheIndex()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "transfers": ["bargeld"]
            }
            """
        );

        try
        {
            Assert.AreEqual(
                RuleSetLoader.Shipped().Fingerprint,
                RuleSetLoader.Load(path).Fingerprint
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void RenamingAVendorChangesTheFingerprintSoTheIndexReExtracts()
    {
        var path = WritePatterns(
            """
            {
              "version": 1,
              "vendors": { "kabelshop.example": "Kabelshop" }
            }
            """
        );

        try
        {
            Assert.AreNotEqual(
                RuleSetLoader.Shipped().Fingerprint,
                RuleSetLoader.Load(path).Fingerprint
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WritePatterns(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"patterns-{Guid.NewGuid():N}.jsonc");
        File.WriteAllText(path, content);
        return path;
    }

    private static MailRecord Mail(string subject, string from) =>
        new("message.emlx", Received, subject, from, string.Empty, "RECHNUNG 4711");
}

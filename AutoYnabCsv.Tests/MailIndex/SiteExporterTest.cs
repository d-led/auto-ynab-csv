using System.Globalization;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;
using AutoYnabCsv.MailIndex.Web;

namespace Tests;

[TestClass]
public class SiteExporterTest
{
    private static readonly DateTimeOffset Received = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void TheSiteHoldsMailsAndBankEntriesAndLinksTheMatchedOnes()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"site-{Guid.NewGuid():N}"
        );

        try
        {
            var entry = new CorrelationEntry(
                0,
                new DateOnly(2026, 10, 2),
                "PayPal Europe S.a.r.l.",
                177.29m,
                "1050000000001/. Thomann GmbH, Ihr Einkauf bei Thomann GmbH"
            );

            var export = SiteExporter.Export(
                directory,
                [Mail()],
                [new BankSource("transactions.csv", "n26", [entry])],
                Audit()
            );

            var json = File.ReadAllText(export.JsonPath);
            StringAssert.Contains(json, "\"kind\":\"mail\"");
            StringAssert.Contains(json, "\"kind\":\"bank\"");
            StringAssert.Contains(json, "\"merchant\":\"Thomann GmbH\"");
            StringAssert.Contains(json, "\"relatedIds\":[\"mail:message.emlx\"]");

            var html = File.ReadAllText(export.HtmlPath);
            StringAssert.Contains(html, "id=\"data\"");
            StringAssert.Contains(html, "scoreToken");
            StringAssert.Contains(html, "Thomann GmbH");
            StringAssert.Contains(html, "Audit:");
            StringAssert.Contains(File.ReadAllText(export.AuditPath), "/tmp/mail-index.sqlite");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void AQueryNeedsAllOfItsTokens()
    {
        var html = SiteTemplate.Html("{}", "now", "never", string.Empty);

        StringAssert.Contains(html, "function scoreGroup");
        StringAssert.Contains(html, "if (best === 0) return 0;");
    }

    [TestMethod]
    public void EachFieldInTheListSaysWhatItIs()
    {
        var html = SiteTemplate.Html("{}", "now", "never", string.Empty);

        StringAssert.Contains(html, "class=\"f\"");
        StringAssert.Contains(html, "['merchant', d => d.merchant]");
        StringAssert.Contains(html, "class=\"evidence\"");
    }

    [TestMethod]
    public void TheRunFolderHoldsTheRowsToImportWithTheMailContextInTheMemo()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"site-{Guid.NewGuid():N}"
        );

        try
        {
            var export = SiteExporter.Export(
                directory,
                [Mail()],
                [new BankSource("transactions.csv", "n26", [Entry()])],
                Audit()
            );

            var csv = File.ReadAllText(export.CsvPaths.Single());
            StringAssert.Contains(csv, "Date,Payee,Memo,Inflow,Outflow");
            StringAssert.Contains(csv, "Kabel 3,50 €");
            StringAssert.Contains(csv, "Order 123456789");
            StringAssert.Contains(csv, "via thomann");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void TwoMailsMatchedOnlyByAmountLeaveTheRowAsTheBankWroteIt()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"site-{Guid.NewGuid():N}"
        );

        try
        {
            var first = Mail();
            var second = first with
            {
                Mail = first.Mail with
                {
                    Path = "second.emlx",
                    MessageId = "<def-456@thomann.de>",
                },
            };
            var amountOnly = new CorrelationEntry(
                0,
                new DateOnly(2026, 10, 2),
                "Thomann GmbH",
                177.29m,
                string.Empty
            );

            var export = SiteExporter.Export(
                directory,
                [first, second],
                [new BankSource("transactions.csv", "n26", [amountOnly])],
                Audit()
            );

            var csv = File.ReadAllText(export.CsvPaths.Single());
            Assert.IsFalse(csv.Contains("Kabel 3,50 €", StringComparison.Ordinal));
            Assert.IsFalse(csv.Contains("via thomann", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void TheConfirmationAndTheDeliveryNoteBothExplainTheOrder()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"site-{Guid.NewGuid():N}"
        );

        try
        {
            var confirmation = Mail();
            var delivery = confirmation with
            {
                Mail = confirmation.Mail with
                {
                    Path = "delivered.emlx",
                    Subject = "Your package was delivered",
                    ReceivedAt = Received.AddDays(21),
                    MessageId = "<def-456@thomann.de>",
                },
                Fields = new Dictionary<string, string>
                {
                    ["orderNumber"] = "123456789",
                    ["items"] = "Saitensatz 12,50 €",
                },
            };

            var export = SiteExporter.Export(
                directory,
                [confirmation, delivery],
                [new BankSource("transactions.csv", "n26", [Entry()])],
                Audit()
            );

            var csv = File.ReadAllText(export.CsvPaths.Single());
            StringAssert.Contains(csv, "Kabel 3,50 € · Saitensatz 12,50 €");
            StringAssert.Contains(csv, "Order 123456789");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void OnlyTheMailsTheRowsUsedAreOnThePage()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"site-{Guid.NewGuid():N}"
        );

        try
        {
            var export = SiteExporter.Export(
                directory,
                [Mail(), Unrelated()],
                [new BankSource("transactions.csv", "n26", [Entry()])],
                Audit()
            );

            var json = File.ReadAllText(export.JsonPath);
            StringAssert.Contains(json, "Ihre Thomann Bestellung");
            Assert.IsFalse(json.Contains("Newsletter", StringComparison.Ordinal));
            Assert.AreEqual(1, export.EvidenceMails);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void EachRowLinksBackToTheMailInAppleMail()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"site-{Guid.NewGuid():N}"
        );

        try
        {
            var export = SiteExporter.Export(
                directory,
                [Mail()],
                [new BankSource("transactions.csv", "n26", [Entry()])],
                Audit()
            );

            StringAssert.Contains(
                File.ReadAllText(export.JsonPath),
                "\"mailUrl\":\"message://%3Cabc-123%40thomann.de%3E\""
            );
            StringAssert.Contains(File.ReadAllText(export.HtmlPath), "d.mailUrl");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void AMailLinkWrapsTheIdInTheBracketsAppleMailNeeds()
    {
        Assert.AreEqual(
            "message://%3C302.1234567%40amazon.de%3E",
            MailLink.Of("302.1234567@amazon.de")
        );
    }

    [TestMethod]
    public void AMailWithoutAMessageIdHasNoLink()
    {
        Assert.AreEqual(string.Empty, MailLink.Of(string.Empty));
    }

    [TestMethod]
    public void TheHeaderNamesTheSpanTheIndexCovers()
    {
        var html = SiteTemplate.Html("{}", "now", "2024-01-01 … 2026-10-19 (12 messages)", string.Empty);

        StringAssert.Contains(html, "covers 2024-01-01 … 2026-10-19 (12 messages)");
    }

    [TestMethod]
    public void DatesStayGregorianOnAMachineWithAnotherCalendar()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"site-{Guid.NewGuid():N}"
        );

        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            var export = SiteExporter.Export(
                directory,
                [Mail()],
                [new BankSource("transactions.csv", "n26", [Entry()])],
                Audit() with
                {
                    GeneratedAt = Received,
                    Mail = new MailAudit(
                        "/tmp/mail-index.sqlite",
                        "/tmp/Mail",
                        10,
                        1,
                        Received,
                        Received,
                        Received,
                        "fp"
                    ),
                }
            );

            StringAssert.Contains(File.ReadAllText(export.JsonPath), "\"date\":\"2026-10-02\"");
            StringAssert.Contains(File.ReadAllText(export.CsvPaths.Single()), "\n2026-10-02,");
            StringAssert.Contains(File.ReadAllText(export.HtmlPath), "2026-10-02");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FlowsStaySeparatePerDirection()
    {
        var day = new DateOnly(2026, 10, 2);

        var banks = new[]
        {
            new BankSource(
                "a_DE11111111111111111111.csv",
                "dkb.giro",
                [
                    new CorrelationEntry(0, day, "Some Person", 100m, string.Empty, IsInflow: false),
                    new CorrelationEntry(2, day, "Some Person", 40m, string.Empty, IsInflow: true),
                ]
            ),
            new BankSource(
                "b_DE22222222222222222222.csv",
                "dkb.giro",
                [
                    new CorrelationEntry(1, day, "Some Person", 100m, string.Empty, IsInflow: true),
                    new CorrelationEntry(3, day, "Some Person", 40m, string.Empty, IsInflow: false),
                ]
            ),
        };

        var flows = SiteExporter.Flows(banks);

        Assert.AreEqual(2, flows.Count);
        Assert.AreEqual(100m, flows.Single(flow => flow.From == "a · 1111").Total);
        Assert.AreEqual(40m, flows.Single(flow => flow.From == "b · 2222").Total);
        Assert.AreEqual(1, flows.Single(flow => flow.From == "a · 1111").Count);
    }

    [TestMethod]
    public void ARowOfAnAccountTransferIsNamedAfterTheAccountTheMoneyCrossedTo()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            $"site-{Guid.NewGuid():N}"
        );

        try
        {
            var day = new DateOnly(2026, 10, 2);
            var banks = new[]
            {
                new BankSource(
                    "giro_DE11111111111111111111.csv",
                    "dkb.giro",
                    [new CorrelationEntry(0, day, "Some Person", 100m, "Sparen")]
                ),
                new BankSource(
                    "tagesgeld_DE22222222222222222222.csv",
                    "dkb.tagesgeld",
                    [new CorrelationEntry(1, day, "Some Person", 100m, IsInflow: true)]
                ),
            };

            var export = SiteExporter.Export(directory, [], banks, Audit());

            StringAssert.Contains(Exported(export, "giro"), "Transfer : tagesgeld · 2222");
            StringAssert.Contains(Exported(export, "tagesgeld"), "Transfer : giro · 1111");
            StringAssert.Contains(File.ReadAllText(export.JsonPath), "Transfer : giro");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string Exported(SiteExport export, string source) =>
        File.ReadAllText(export.CsvPaths.Single(path => Path.GetFileName(path).StartsWith(source)));

    private static MailFacts Unrelated() =>
        new(
            new MailRecord(
                "newsletter.emlx",
                new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
                "Newsletter",
                "news@example.com",
                string.Empty,
                "Nothing about a purchase"
            ),
            "example.com",
            [],
            [5.00m],
            new Dictionary<string, string>()
        );

    private static CorrelationEntry Entry() =>
        new(0, new DateOnly(2026, 10, 2), "ISSUER", 177.29m, "Order 123456789");

    private static SiteAudit Audit() =>
        new(
            DateTimeOffset.UnixEpoch,
            "test",
            new MailAudit("/tmp/mail-index.sqlite", "/tmp/Mail", 10, 0, null, null, null, "fp"),
            [new BankAudit("transactions.csv", "n26", 1)],
            []
        );

    private static MailFacts Mail() =>
        new(
            new MailRecord(
                "message.emlx",
                Received,
                "Ihre Thomann Bestellung",
                "order@thomann.de",
                string.Empty,
                "Kabel 3,50 €",
                "<abc-123@thomann.de>"
            ),
            "Thomann",
            ["123456789"],
            [177.29m],
            new Dictionary<string, string>
            {
                ["orderNumber"] = "123456789",
                ["items"] = "Kabel 3,50 €\nSaitensatz 12,50 €",
            }
        );
}

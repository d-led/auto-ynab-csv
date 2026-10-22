using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;
using AutoYnabCsv.MailIndex.Suggestions;

namespace Tests;

[TestClass]
public class RuleSuggestionWriterTest
{
    private static readonly DateTimeOffset Received = new(2024, 12, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ProposesAnExtractRulePerSenderAndAnAliasForOpaquePayees()
    {
        var text = RuleSuggestionWriter.Write([Amazon(), Opaque()]);

        StringAssert.Contains(text, "[[extract]]");
        StringAssert.Contains(text, "sender = \"amazon.de\"");
        StringAssert.Contains(text, "vendor = \"Amazon\"");
        StringAssert.Contains(text, "[[alias]]");
        StringAssert.Contains(text, "payeeContains = \"PayPal Europe S.a.r.l.\"");
        StringAssert.Contains(text, "vendor = \"Kleinanzeigen\"");
    }

    [TestMethod]
    public void DoesNotProposeAnAliasWhenThePayeeAlreadyNamesTheVendor()
    {
        var text = RuleSuggestionWriter.Write([Amazon()]);

        Assert.IsFalse(text.Contains("[[alias]]", StringComparison.Ordinal));
    }

    private static Match Amazon() =>
        new(
            new CorrelationEntry(0, new DateOnly(2024, 12, 25), "Amazon EU S.a.r.l.", 42.99m),
            Facts("order-update@amazon.de", "Amazon", 42.99m),
            0,
            new Evidence(EvidenceKind.AmountAndCounterparty, "42.99 · Amazon named")
        );

    private static Match Opaque() =>
        new(
            new CorrelationEntry(1, new DateOnly(2024, 12, 25), "PayPal Europe S.a.r.l.", 25m),
            Facts("service@mail.kleinanzeigen.de", "Kleinanzeigen", 25m),
            0,
            new Evidence(EvidenceKind.AmountAndCounterparty, "25.00 · Kleinanzeigen named")
        );

    private static MailFacts Facts(string from, string vendor, decimal amount) =>
        new(
            new MailRecord("message.emlx", Received, "Subject", from, string.Empty, string.Empty),
            vendor,
            [],
            [amount],
            new Dictionary<string, string>()
        );
}

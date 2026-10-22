using System.Text.RegularExpressions;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class ExtractionRuleSetTest
{
    private static readonly DateTimeOffset Received = new(2024, 12, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void APatternThatWouldBacktrackForHoursIsStoppedAndFails()
    {
        // A rule with nested quantifiers can chew on one message for hours, and a silent hang
        // names nothing. Half a second, then it says which rule is broken by failing loudly.
        var rules = Rules(
            new ExtractionRule(
                "greedy",
                Sender: "shop",
                Fields: [new FieldExtraction("total", "(a+)+$")]
            )
        );
        var mail = Mail("anything", "order@shop.de", new string('a', 40) + "b");

        Assert.ThrowsExactly<RegexMatchTimeoutException>(() => rules.Extract(mail));
    }

    [TestMethod]
    public void AFittingRuleFillsItsNamedFields()
    {
        var rules = Rules(
            new ExtractionRule(
                "amazon",
                Sender: "amazon",
                Subject: "Bestellung",
                Vendor: "Amazon",
                Fields:
                [
                    new FieldExtraction("orderNumber", @"\b\d{3}-\d{7}-\d{7}\b"),
                    new FieldExtraction("total", @"EUR\s*([0-9]+,[0-9]{2})"),
                ]
            )
        );
        var mail = Mail("Ihre Bestellung 302-1234567-8901234", "order@amazon.de", "Summe: EUR 42,99");

        var fields = rules.Extract(mail);

        Assert.AreEqual("302-1234567-8901234", fields["orderNumber"]);
        Assert.AreEqual("42,99", fields["total"]);
        Assert.AreEqual("Amazon", rules.VendorOf(mail));
    }

    [TestMethod]
    public void ARuleThatDoesNotFitFillsNothing()
    {
        var rules = Rules(
            new ExtractionRule(
                "amazon",
                Sender: "amazon",
                Fields: [new FieldExtraction("orderNumber", @"\d+")]
            )
        );

        var fields = rules.Extract(Mail("Hello", "friend@example.com", "nothing"));

        Assert.AreEqual(0, fields.Count);
        Assert.IsNull(rules.VendorOf(Mail("Hello", "friend@example.com", "nothing")));
    }

    [TestMethod]
    public void TheFirstRuleToFillAFieldOwnsIt()
    {
        var rules = Rules(
            new ExtractionRule("first", Fields: [new FieldExtraction("orderNumber", "AAA")]),
            new ExtractionRule("second", Fields: [new FieldExtraction("orderNumber", "BBB")])
        );

        var fields = rules.Extract(Mail("AAA BBB", "a@b.c", string.Empty));

        Assert.AreEqual("AAA", fields["orderNumber"]);
    }

    [TestMethod]
    public void AnItemFieldKeepsEveryItemRatherThanTheFirst()
    {
        var rules = Rules(
            new ExtractionRule(
                "amazon",
                Sender: "amazon",
                Fields:
                [
                    new FieldExtraction(
                        FieldExtraction.ItemsName,
                        @"(?m)^\s*\d{1,2}\s*x\s+(?<item>[^\r\n]+)$"
                    ),
                ]
            )
        );

        var fields = rules.Extract(
            Mail("Bestellung", "order@amazon.de", "1 x Kabel 3,50 €\n2 x Stecker 1,20 €")
        );

        CollectionAssert.AreEqual(
            new[] { "Kabel 3,50 €", "Stecker 1,20 €" },
            fields[FieldExtraction.ItemsName].Split('\n')
        );
    }

    [TestMethod]
    public void AnInvalidPatternNamesTheRule()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            Rules(new ExtractionRule("broken", Fields: [new FieldExtraction("x", "(")]))
        );

        StringAssert.Contains(exception.Message, "broken");
    }

    private static ExtractionRuleSet Rules(params ExtractionRule[] rules) => new(rules);

    private static MailRecord Mail(string subject, string from, string body) =>
        new("message.emlx", Received, subject, from, string.Empty, body);
}

using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class CorrelatorTest
{
    private static readonly DateOnly Day = new(2024, 12, 25);

    [TestMethod]
    public void ANumberBothQuoteIsTheEvidenceEvenWhenTheAmountMoved()
    {
        var entry = new CorrelationEntry(0, Day, "Amazon", 41.99m, "Order 028-1234567-8901234");

        var matches = Correlator.Match(
            [entry],
            [Facts(42.99m, At(25, 9), orderNumbers: ["028-1234567-8901234"])]
        );

        Assert.AreEqual(1, matches.Count);
        Assert.IsTrue(matches[0].IsExact);
        Assert.AreEqual("reference 028-1234567-8901234", matches[0].Evidence.Describe());
    }

    [TestMethod]
    public void TheSameAmountAndACounterpartyTheRowNamesIsEvidence()
    {
        var entry = new CorrelationEntry(0, Day, "PayPal Europe", 25m);

        var matches = Correlator.Match([entry], [Facts(25m, At(25, 9), vendor: "PayPal")]);

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual(EvidenceKind.AmountAndCounterparty, matches[0].Evidence.Kind);
        Assert.AreEqual("amount 25.00 · PayPal named", matches[0].Evidence.Describe());
    }

    [TestMethod]
    public void AnAmountWithoutACounterpartyTheRowNamesIsNoEvidence()
    {
        var entry = new CorrelationEntry(0, Day, "Some payee", 9.99m);

        var matches = Correlator.Match([entry], [Facts(9.99m, At(25, 9), vendor: string.Empty)]);

        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void TwoEquallyPlausibleMailsStayAmbiguous()
    {
        var entry = new CorrelationEntry(0, Day, "PayPal Europe", 9.99m);

        var matches = Correlator.Match(
            [entry],
            [
                Facts(9.99m, At(25, 9), vendor: "PayPal"),
                Facts(9.99m, At(25, 18), vendor: "PayPal"),
            ]
        );

        Assert.AreEqual(2, matches.Count);
        Assert.AreEqual(matches[0].Evidence, matches[1].Evidence);
        Assert.AreEqual(matches[0].DayDistance, matches[1].DayDistance);
    }

    [TestMethod]
    public void AnExactReferenceIsRankedAboveAnAmountMatch()
    {
        var entry = new CorrelationEntry(0, Day, "Amazon", 9.99m, "Order 028-1234567-8901234");

        var matches = Correlator.Match(
            [entry],
            [
                Facts(9.99m, At(25, 9), vendor: "Amazon"),
                Facts(10.99m, At(26, 9), orderNumbers: ["028-1234567-8901234"]),
            ]
        );

        Assert.AreEqual(2, matches.Count);
        Assert.IsTrue(matches[0].IsExact);
        Assert.IsFalse(matches[1].IsExact);
    }

    [TestMethod]
    public void AMatchOutsideTheWindowIsIgnored()
    {
        var entry = new CorrelationEntry(0, Day, "PayPal Europe", 9.99m);

        var matches = Correlator.Match([entry], [Facts(9.99m, At(30, 9))], windowDays: 2);

        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void ANumberBothQuoteMatchesHoweverLateTheMailArrives()
    {
        // The order was booked in December; the parcel was delivered in January.
        var entry = new CorrelationEntry(0, Day, "Amazon", 20.58m, "000-0000000-0000001");
        var delivered = new DateTimeOffset(2025, 1, 13, 15, 0, 0, TimeSpan.Zero);

        var matches = Correlator.Match(
            [entry],
            [Facts(20.58m, delivered, vendor: "Amazon", orderNumbers: ["000-0000000-0000001"])]
        );

        Assert.AreEqual(1, matches.Count);
        Assert.IsTrue(matches[0].IsExact);
    }

    [TestMethod]
    public void ADifferentAmountNeverMatches()
    {
        var entry = new CorrelationEntry(0, Day, "PayPal Europe", 9.99m);

        var matches = Correlator.Match([entry], [Facts(19.99m, At(25, 9))]);

        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void AMailThatQuotesTheAmountTwiceIsOneMatch()
    {
        var entry = new CorrelationEntry(0, Day, "PayPal Europe", 9.99m);
        var twice = Facts(9.99m, At(25, 9), vendor: "PayPal") with { Amounts = [9.99m, 9.99m] };

        var matches = Correlator.Match([entry], [twice]);

        Assert.AreEqual(1, matches.Count);
    }

    private static DateTimeOffset At(int day, int hour) =>
        new(2024, 12, day, hour, 0, 0, TimeSpan.Zero);

    private static MailFacts Facts(
        decimal amount,
        DateTimeOffset received,
        string vendor = "PayPal",
        IReadOnlyList<string>? orderNumbers = null
    ) =>
        new(
            new MailRecord(
                $"message-{received:MMdd-HHmm}.emlx",
                received,
                "Subject",
                "service@paypal.de",
                string.Empty,
                string.Empty
            ),
            vendor,
            orderNumbers ?? [],
            [amount],
            new Dictionary<string, string>()
        );
}

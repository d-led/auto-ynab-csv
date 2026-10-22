using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

/// <summary>
/// The second pass reads the stubs Mail leaves for messages whose body is still on the server.
/// A stub has headers only, so the judgement is: does the day fit, and does what the headers say
/// mention the entry?
/// </summary>
[TestClass]
public class HeaderMailFinderTest
{
    private static readonly DateOnly Day = new(2026, 10, 2);

    [TestMethod]
    public void AStubWhoseSubjectCarriesTheTransferNumberIsFound()
    {
        var stubs = new[]
        {
            Stub("/mail/hit.emlx", Day, "Ihre Überweisung 1050000000001"),
            Stub("/mail/other.emlx", Day, "Ihr Newsletter"),
        };

        var found = HeaderMailFinder.Search(Query(["1050000000001"], "Thomann GmbH"), 10, stubs);

        CollectionAssert.AreEqual(new[] { "/mail/hit.emlx" }, found.ToArray());
    }

    [TestMethod]
    public void AStubIsFoundByTheMerchantTheTransactionNames()
    {
        var stubs = new[] { Stub("/mail/thomann.emlx", Day, "Ihre Bestellung bei Thomann") };

        var found = HeaderMailFinder.Search(Query([], "Thomann GmbH"), 10, stubs);

        CollectionAssert.AreEqual(new[] { "/mail/thomann.emlx" }, found.ToArray());
    }

    [TestMethod]
    public void AStubFromAMonthAwayCannotStandInAsEvidence()
    {
        var stubs = new[] { Stub("/mail/july.emlx", new DateOnly(2026, 7, 2), "Bestellung Thomann") };

        var found = HeaderMailFinder.Search(Query([], "Thomann GmbH"), 10, stubs);

        Assert.AreEqual(0, found.Count);
    }

    [TestMethod]
    public void TheDayFurthestFromTheTransactionComesLast()
    {
        var stubs = new[]
        {
            Stub("/mail/far.emlx", Day.AddDays(-2), "Thomann Bestellung", "302.far@thomann.de"),
            Stub("/mail/near.emlx", Day, "Thomann Bestellung", "302.near@thomann.de"),
        };

        var found = HeaderMailFinder.Search(Query([], "Thomann GmbH"), 10, stubs);

        CollectionAssert.AreEqual(new[] { "/mail/near.emlx", "/mail/far.emlx" }, found.ToArray());
    }

    [TestMethod]
    public void OnlyAFewCandidatesAreOffered()
    {
        var stubs = Enumerable
            .Range(0, 5)
            .Select(index => Stub($"/mail/{index}.emlx", Day, "Thomann Bestellung", $"302.{index}@thomann.de"))
            .ToArray();

        Assert.AreEqual(3, HeaderMailFinder.Search(Query([], "Thomann GmbH"), 3, stubs).Count);
    }

    [TestMethod]
    public void TheSameMessageInTwoMailboxesIsOneCandidate()
    {
        var stubs = new[]
        {
            Stub("/mail/inbox.emlx", Day, "Thomann Bestellung", id: "302.1@thomann.de"),
            Stub("/mail/all.emlx", Day, "Thomann Bestellung", id: "302.1@thomann.de"),
        };

        Assert.AreEqual(1, HeaderMailFinder.Search(Query([], "Thomann GmbH"), 10, stubs).Count);
    }

    [TestMethod]
    public void AnEntryWithNothingToSearchedForFindsNothing()
    {
        var stubs = new[] { Stub("/mail/thomann.emlx", Day, "Thomann Bestellung") };

        Assert.AreEqual(0, HeaderMailFinder.Search(Query([], string.Empty), 10, stubs).Count);
    }

    private static MailQuery Query(IReadOnlyList<string> identifiers, string phrase) =>
        new(identifiers, phrase, Day, 2);

    private static MailFacts Stub(
        string path,
        DateOnly day,
        string subject,
        string id = "302.1234567@thomann.de"
    ) =>
        new(
            new MailRecord(
                path,
                new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0))),
                subject,
                "order@thomann.de",
                "Thomann",
                string.Empty,
                id
            ),
            "Thomann",
            [],
            [],
            new Dictionary<string, string>()
        );
}

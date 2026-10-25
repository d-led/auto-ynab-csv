using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class MatcherTest
{
    [TestMethod]
    public void AMovementBothSidesShowIsMatchedOnce()
    {
        var outcome = Matcher.Match([Row(0, "2026-10-23", -45.81, "Kaufland")], [Plan("t1", "2026-10-23", -45.81, "Kaufland")]);

        Assert.AreEqual(1, outcome.Matches.Count);
        Assert.AreEqual("t1", outcome.Matches[0].Transaction.Id);
        Assert.AreEqual(0, outcome.InBankOnly.Count);
        Assert.AreEqual(0, outcome.InPlanOnly.Count);
    }

    [TestMethod]
    public void ACardChargedOnFridayAndSettledOnMondayIsStillTheSameMovement()
    {
        var outcome = Matcher.Match([Row(0, "2026-10-26", -45.81, "Kaufland")], [Plan("t1", "2026-10-23", -45.81, "Kaufland")]);

        Assert.AreEqual(1, outcome.Matches.Count, "three days apart is inside the window");
        Assert.AreEqual(3, outcome.Matches[0].DayDistance);
    }

    [TestMethod]
    public void ARowThePlanDoesNotHaveIsMissing()
    {
        var outcome = Matcher.Match([Row(0, "2026-10-23", -19.71, "PayPal")], []);

        Assert.AreEqual(1, outcome.InBankOnly.Count);
        Assert.AreEqual("PayPal", outcome.InBankOnly[0].Payee);
    }

    [TestMethod]
    public void ATransactionTheExportDoesNotHaveIsExtra()
    {
        var outcome = Matcher.Match([], [Plan("t1", "2026-10-23", -19.71, "PayPal")]);

        Assert.AreEqual(1, outcome.InPlanOnly.Count);
    }

    [TestMethod]
    public void TwoCoffeesOfTheSamePriceTakeOneTransactionEach()
    {
        var outcome = Matcher.Match(
            [Row(0, "2026-10-23", -3.50, "Cafe"), Row(1, "2026-10-23", -3.50, "Cafe")],
            [Plan("t1", "2026-10-23", -3.50, "Cafe"), Plan("t2", "2026-10-23", -3.50, "Cafe")]
        );

        Assert.AreEqual(2, outcome.Matches.Count, "a transaction may only settle one row");
        CollectionAssert.AreEquivalent(new[] { "t1", "t2" }, outcome.Matches.Select(match => match.Transaction.Id).ToArray());
    }

    [TestMethod]
    public void AThirdRowWithOnlyTwoTransactionsStaysMissing()
    {
        var outcome = Matcher.Match(
            [Row(0, "2026-10-23", -3.50, "Cafe"), Row(1, "2026-10-23", -3.50, "Cafe"), Row(2, "2026-10-23", -3.50, "Cafe")],
            [Plan("t1", "2026-10-23", -3.50, "Cafe"), Plan("t2", "2026-10-23", -3.50, "Cafe")]
        );

        Assert.AreEqual(2, outcome.Matches.Count);
        Assert.AreEqual(1, outcome.InBankOnly.Count);
    }

    [TestMethod]
    public void ADifferentAmountIsNeverTheSameMovement()
    {
        var outcome = Matcher.Match([Row(0, "2026-10-23", -45.81, "Kaufland")], [Plan("t1", "2026-10-23", -45.82, "Kaufland")]);

        Assert.AreEqual(0, outcome.Matches.Count);
    }

    [TestMethod]
    public void AMovementAFortnightAwayIsNotMatched()
    {
        var outcome = Matcher.Match([Row(0, "2026-10-23", -45.81, "Kaufland")], [Plan("t1", "2026-10-09", -45.81, "Kaufland")]);

        Assert.AreEqual(0, outcome.Matches.Count, "two weeks is a different purchase, not a late posting");
    }

    internal static BankRow Row(int index, string day, decimal amount, string payee = "Shop") =>
        new(DateOnly.Parse(day), amount, payee, "memo", "export.csv", index);

    /// <summary>
    /// Money in a test reads as the bank wrote it — <c>-45.81</c> — and two decimal places convert
    /// to <see cref="decimal"/> without noise, so the amount is exact and not nearly right.
    /// </summary>
    internal static BankRow Row(int index, string day, double amount, string payee = "Shop") =>
        Row(index, day, (decimal)amount, payee);

    internal static YnabTransaction Plan(
        string id,
        string day,
        decimal amount,
        string payee,
        string? transferAccountId = null
    ) =>
        new(
            Id: id,
            Date: DateOnly.Parse(day),
            Amount: amount,
            AccountId: "acc",
            AccountName: "Girokonto",
            PayeeId: null,
            PayeeName: payee,
            CategoryId: null,
            CategoryName: null,
            Memo: null,
            Cleared: true,
            Approved: true,
            TransferAccountId: transferAccountId,
            ImportId: null
        );

    internal static YnabTransaction Plan(
        string id,
        string day,
        double amount,
        string payee,
        string? transferAccountId = null
    ) => Plan(id, day, (decimal)amount, payee, transferAccountId);
}

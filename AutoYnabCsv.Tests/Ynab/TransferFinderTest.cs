using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class TransferFinderTest
{
    private const string Giro = "DE00000000000000000001";
    private const string Together = "DE00000000000000000004";
    private const string N26 = "DE00000000000000000026";

    private static readonly OwnAccounts Own = OwnAccounts.Of(
        [Statement("Girokonto · 9004", Giro), Statement("Girokonto · 3784", Together)]
    );

    [TestMethod]
    public void ARowTheBankHasInAnotherAccountIsOfferedAsAMove()
    {
        var moves = TransferFinder.Reaccountings(
            [new RowOf("Girokonto · 3784", Row(0, "2026-01-14", -300m, Giro))],
            [("Girokonto · 9004", Sample.T("t1", "2026-01-14", -300m, "Dmitry"))]
        );

        Assert.AreEqual(1, moves.Count);
        Assert.AreEqual("Girokonto · 9004", moves[0].FromLabel);
        Assert.AreEqual("Girokonto · 3784", moves[0].ToLabel);
    }

    [TestMethod]
    public void ARowThePlanAlreadyHasInThatAccountIsNotMoved()
    {
        var moves = TransferFinder.Reaccountings(
            [new RowOf("Girokonto · 9004", Row(0, "2026-01-14", -300m, Giro))],
            [("Girokonto · 9004", Sample.T("t1", "2026-01-14", -300m, "Dmitry"))]
        );

        Assert.AreEqual(0, moves.Count, "it is already where the bank has it");
    }

    [TestMethod]
    public void TheNearestDayIsTheOneThatExplainsTheMove()
    {
        var moves = TransferFinder.Reaccountings(
            [new RowOf("Girokonto · 3784", Row(0, "2026-01-14", -300m, Giro))],
            [
                ("Girokonto · 9004", Sample.T("far", "2026-01-18", -300m, "Dmitry")),
                ("Girokonto · 9004", Sample.T("near", "2026-01-14", -300m, "Dmitry")),
            ]
        );

        Assert.AreEqual("near", moves.Single().Transaction.Id);
    }

    [TestMethod]
    public void OnePlanRowCannotExplainTwoBankRows()
    {
        var moves = TransferFinder.Reaccountings(
            [
                new RowOf("Girokonto · 3784", Row(0, "2026-01-14", -300m, Giro)),
                new RowOf("Girokonto · 3784", Row(1, "2026-01-15", -300m, Giro)),
            ],
            [("Girokonto · 9004", Sample.T("t1", "2026-01-14", -300m, "Dmitry"))]
        );

        Assert.AreEqual(1, moves.Count);
    }

    [TestMethod]
    public void TwoHalvesOfATransferAreOneMovement()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Girokonto · 9004", Row(0, "2026-01-14", -300m, Together)),
                new RowOf("Girokonto · 3784", Row(0, "2026-01-14", 300m, Giro)),
            ],
            Own
        );

        Assert.AreEqual(1, transfers.Count);
        Assert.IsTrue(transfers[0].ConfirmedByAccountNumber, "both exports name the other side");
        Assert.AreEqual("Girokonto · 3784", transfers[0].ToLabel);
    }

    [TestMethod]
    public void TheAccountNumberDecidesWhichOfTwoEqualAmountsIsThePartner()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Girokonto · 9004", Row(0, "2026-01-14", -300m, Together)),
                new RowOf("Girokonto · 9004", Row(1, "2026-01-20", -300m, Together)),
                new RowOf("Girokonto · 3784", Row(0, "2026-01-14", 300m, Giro)),
            ],
            Own
        );

        Assert.AreEqual(1, transfers.Count);
        Assert.AreEqual(new DateOnly(2026, 1, 14), transfers[0].Outgoing.Date, "the day the export names, not the next one");
    }

    [TestMethod]
    public void APaymentToSomebodyElseHasNoSecondHalf()
    {
        var transfers = TransferFinder.Pair(
            [new RowOf("Girokonto · 9004", Row(0, "2026-01-14", -300m, "DE99999999999999999999"))],
            Own
        );

        Assert.AreEqual(0, transfers.Count);
    }

    [TestMethod]
    public void AMaskedCardNumberIsNotOneOfYourAccounts()
    {
        Assert.IsNull(Own.LabelOf("4748 •••• •••• 9158"));
        Assert.IsNull(Own.LabelOf("1052774781"), "a short internal number is not an account anybody transfers to");
    }

    [TestMethod]
    public void AnAccountNumberWrittenWithSpacesIsStillRecognised()
    {
        Assert.AreEqual("Girokonto · 9004", Own.LabelOf(Spaced(Giro)));
    }

    [TestMethod]
    public void AnAccountNumberTheExportNeverPrintsCanBeGivenByHand()
    {
        var own = OwnAccounts.Of(
            [Statement("Girokonto · 9004", Giro)],
            new Dictionary<string, string> { [Spaced(N26)] = "n26" }
        );

        Assert.AreEqual("n26", own.LabelOf(N26), "n26's download prints no account number of its own");
    }

    [TestMethod]
    public void ARowSaysWhichOfYourAccountsItMovesMoneyTo()
    {
        var statement = new BankStatement(
            "export.csv",
            "Girokonto · 9004",
            Statement("Girokonto · 9004", Giro).Statement,
            [Row(0, "2026-01-14", -300m, Together), Row(1, "2026-01-15", -6.82m, "DE99999999999999999999")]
        );

        var targets = TransferFinder.TargetsOf(statement, Own);

        Assert.AreEqual("Girokonto · 3784", targets[0]);
        Assert.IsFalse(targets.ContainsKey(1), "a supermarket is not one of your accounts");
    }

    /// <summary>Banks print account numbers in groups of four; the same number without the groups.</summary>
    private static string Spaced(string iban) =>
        $"{iban[..4]} {iban[4..8]} {iban[8..12]} {iban[12..16]} {iban[16..]}";

    [TestMethod]
    public void TwoLegsThatNameEachOtherAreOneMovementOnlyThePlanHas()
    {
        var pairs = TransferFinder.OrphanPairs(
            [
                Plan("w1", -500m, "acc-giro", "acc-together"),
                Plan("w2", 500m, "acc-together", "acc-giro"),
            ],
            accountId => accountId
        );

        Assert.AreEqual(1, pairs.Count);
        Assert.AreEqual("w1", pairs[0].Outgoing.Id);
        Assert.AreEqual("acc-together", pairs[0].ToLabel);
    }

    [TestMethod]
    public void ALegWithoutItsPartnerAtTheOtherEndIsNotAPair()
    {
        var pairs = TransferFinder.OrphanPairs([Plan("w1", -500m, "acc-giro", "acc-together")], accountId => accountId);

        Assert.AreEqual(0, pairs.Count, "money left and arrived nowhere is a different problem");
    }

    [TestMethod]
    public void ATransferBetweenTheAccountsTheBankNamesIsNotAMismatch()
    {
        var wrong = new PlanTransferPair(
            Plan("w1", -500m, "Giro", "Together"),
            Plan("w2", 500m, "Together", "Giro"),
            "Giro",
            "Together"
        );
        var same = new Transfer(Row(0, "2026-01-14", -500m, "x"), Row(1, "2026-01-14", 500m, "x"), "Giro", "Together", 0, true);

        Assert.AreEqual(0, TransferFinder.Repoints([wrong], [same]).Count);
    }

    [TestMethod]
    public void ATransferTheBankMadeElsewhereIsProposedAsItsReplacement()
    {
        var wrong = new PlanTransferPair(
            Plan("w1", -500m, "Giro", "Together"),
            Plan("w2", 500m, "Together", "Giro"),
            "Giro",
            "Together"
        );
        var real = new Transfer(Row(0, "2026-01-14", -500m, "x"), Row(1, "2026-01-14", 500m, "x"), "Giro", "n26", 0, true);

        var repoints = TransferFinder.Repoints([wrong], [real]);

        Assert.AreEqual(1, repoints.Count);
        Assert.AreEqual("n26", repoints[0].Right.ToLabel);
    }

    private static YnabTransaction Plan(string id, decimal amount, string accountId, string? transferAccountId) =>
        new(
            Id: id,
            Date: new DateOnly(2026, 1, 14),
            Amount: amount,
            AccountId: accountId,
            AccountName: accountId,
            PayeeId: null,
            PayeeName: "Transfer",
            CategoryId: null,
            CategoryName: null,
            Memo: null,
            Cleared: true,
            Approved: true,
            TransferAccountId: transferAccountId,
            ImportId: null
        );

    private static BankStatement Statement(string label, string iban) =>
        new(
            "export.csv",
            label,
            new ExportStatement(label, iban, null, null, null, null),
            []
        );

    private static BankRow Row(int index, string day, decimal amount, string counterpartyIban) =>
        new(DateOnly.Parse(day), amount, "Payee", "memo", "export.csv", index, counterpartyIban);
}
